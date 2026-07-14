"""Wrapper for the LLM vision API — classification, bbox detection, and OCR.

Reuses logic from phanloai_the.py and ocr.py but structured as reusable
functions for the backend pipeline.
"""

from __future__ import annotations

import base64
import json
import mimetypes
import re
from pathlib import Path
from typing import Any
import logging
from PIL import Image
import requests


class OCRParseError(ValueError):
    def __init__(self, message: str, raw_text: str):
        super().__init__(message)
        self.raw_text = raw_text

# ── API configuration ──────────────────────────────────────────────────────────

def _load_dotenv() -> None:
    import os
    from pathlib import Path
    paths_to_try = [
        Path(__file__).resolve().parents[2] / ".env",
        Path(__file__).resolve().parents[1] / ".env",
        Path(".env"),
    ]
    for env_path in paths_to_try:
        if env_path.is_file():
            try:
                with open(env_path, "r", encoding="utf-8") as f:
                    for line in f:
                        line = line.strip()
                        if not line or line.startswith("#"):
                            continue
                        if "=" in line:
                            key, val = line.split("=", 1)
                            key = key.strip()
                            val = val.strip().strip('"').strip("'")
                            os.environ[key] = val
                break
            except Exception:
                pass

_load_dotenv()

import os

API_URL = os.getenv("LLM_API_URL", "")
API_KEY = os.getenv("LLM_API_KEY", "")
MODEL = os.getenv("LLM_MODEL", "")
TIMEOUT_SECONDS = 180

# ── Prompts ────────────────────────────────────────────────────────────────────

CLASSIFICATION_PROMPT = """
Phân loại tài liệu hoặc ảnh Việt Nam trong ảnh.

Chỉ chọn đúng một giá trị cho "loai_tai_lieu":
- "Căn cước công dân"
- "Giấy phép lái xe"
- "Giấy đăng ký ô tô"
- "Giấy đăng ký mô tô/xe máy"
- "Đơn A4"
- "Ảnh xe ô tô"
- "Không xác định"

Quy tắc:
- "Căn cước công dân", "Giấy phép lái xe", "Giấy đăng ký ô tô", "Giấy đăng ký mô tô/xe máy" là các loại thẻ nhựa, thẻ giấy hoặc bản điện tử được mở trên màn hình điện thoại.
- "Đơn A4" là văn bản/đơn khổ giấy A4, thường có tiêu đề lớn ở phần đầu như
  "ĐƠN ...", "GIẤY ...", "TỜ KHAI ..." hoặc tên biểu mẫu hành chính.
- Chỉ gọi là "Đơn A4" khi đây là một trang văn bản/biểu mẫu A4, không phải thẻ
  nhựa, giấy tờ xe hoặc căn cước.
- "Ảnh xe ô tô" là ảnh chụp xe ô tô thực tế (có thể nhìn thấy biển số xe),
  KHÔNG phải giấy tờ đăng ký xe. Ảnh có thể là mặt trước, mặt sau, hoặc bên hông xe.
- Với "Đơn A4", đọc chính xác tên đơn/tiêu đề chính ở đầu văn bản và trả vào
  trường "ten_don". Không lấy khẩu hiệu, tên cơ quan, số văn bản hoặc nội dung thân đơn.
- Với các loại khác, "ten_don" phải là chuỗi rỗng.
- Không OCR thông tin cá nhân và không giải thích.
- Chỉ trả JSON hợp lệ:

{
  "loai_tai_lieu": "một giá trị trong danh sách trên",
  "ten_don": "tên đơn nếu là Đơn A4, ngược lại là chuỗi rỗng"
}
""".strip()

BBOX_PROMPT = """
Bạn đang thực hiện tác vụ phát hiện đối tượng thuần túy.

Hãy tìm một giấy tờ Việt Nam chính trong ảnh.

Giấy tờ có thể là:
- thẻ nhựa,
- giấy tờ dạng giấy,
- biểu mẫu khổ A4,
- giấy đăng ký xe Việt Nam,
- hoặc giấy tờ điện tử đang hiển thị trên màn hình điện thoại.

Nhiệm vụ của bạn là xác định vị trí hình học của giấy tờ.
KHÔNG phân tích hoặc sử dụng nội dung chữ trên giấy tờ.

Chỉ trả về MỘT bounding box bao sát toàn bộ phần giấy tờ đang nhìn thấy.

Yêu cầu:

- Bounding box phải bao sát mọi pixel nhìn thấy thuộc về giấy tờ.
- Bám theo mép ngoài cùng của giấy tờ, không chỉ bao vùng chữ.
- Phải bao gồm cả các vùng lề trắng thuộc về giấy tờ.
- KHÔNG cố tình thu nhỏ bounding box.
- KHÔNG để khoảng hở giữa bounding box và mép giấy tờ nhìn thấy.

Nếu một phần giấy tờ nằm ngoài ảnh:
- Chỉ trả bounding box của phần giấy tờ đang nhìn thấy.
- Không được ước lượng hoặc suy đoán phần không nhìn thấy.
- Nếu mép giấy tờ chạm vào mép ảnh thì bounding box cũng phải chạm mép ảnh đó.
  Ví dụ:
  - nếu giấy tờ chạm mép trái ảnh thì x1 phải bằng 0;
  - nếu giấy tờ chạm mép trên ảnh thì y1 phải bằng 0.

Đối với giấy tờ điện tử hiển thị trên điện thoại:
- Chỉ phát hiện phần giấy tờ hoặc thẻ đang hiển thị.
- Bao gồm đầy đủ phần giấy tờ nhìn thấy, kể cả lề trắng.
- Loại trừ viền điện thoại, khung điện thoại, thanh trạng thái nằm ngoài giấy tờ,
  thanh điều hướng nằm ngoài giấy tờ, phản chiếu, ngón tay, bàn tay, mặt bàn,
  bóng đổ và toàn bộ nền xung quanh.

Đối với giấy tờ dạng giấy:
- Bao gồm toàn bộ tờ giấy hoặc thẻ đang nhìn thấy.
- Loại trừ mặt bàn, bàn làm việc, nền, bóng đổ, bàn tay và các vật thể xung quanh.

Dùng tọa độ chuẩn hóa từ 0 đến 1000 theo kích thước toàn bộ ảnh.

Chỉ trả JSON hợp lệ:

{
  "bbox": {
    "x1": 0,
    "y1": 0,
    "x2": 0,
    "y2": 0
  }
}
""".strip()

VALID_TYPES = {
    "Căn cước công dân",
    "Giấy phép lái xe",
    "Giấy đăng ký ô tô",
    "Giấy đăng ký mô tô/xe máy",
    "Đơn A4",
    "Ảnh xe ô tô",
    "Không xác định",
}

OCR_PROMPTS = {
    "Căn cước công dân": """
Ảnh là Căn cước công dân Việt Nam bản giấy hoặc bản điện tử.

Trích xuất các trường đọc được trên thẻ:
- Số định danh cá nhân
- Họ và tên
- Ngày sinh
- Giới tính
- Quốc tịch
- Quê quán
- Nơi thường trú
- Ngày cấp
- Ngày hết hạn
- Đặc điểm nhận dạng nếu có

Chỉ trả JSON hợp lệ. Giữ nguyên chữ đúng như trên thẻ.
Không tự suy đoán, không sửa lỗi, không thêm thông tin không đọc được.
Trường không đọc được phải có giá trị chuỗi rỗng.

{
  "loai_giay_to": "Căn cước công dân",
  "so_dinh_danh_ca_nhan": "",
  "ho_va_ten": "",
  "ngay_sinh": "",
  "gioi_tinh": "",
  "quoc_tich": "",
  "que_quan": "",
  "noi_thuong_tru": "",
  "ngay_cap": "",
  "ngay_het_han": "",`
  "dac_diem_nhan_dang": ""
}
""".strip(),
    "Giấy phép lái xe": """
Ảnh là Giấy phép lái xe Việt Nam bản giấy hoặc bản điện tử.

Trích xuất các trường đọc được:
- Số giấy phép lái xe
- Họ và tên
- Ngày sinh
- Quốc tịch
- Nơi cư trú
- Hạng giấy phép
- Ngày cấp
- Ngày hết hạn
- Nơi cấp

Chỉ trả JSON hợp lệ. Giữ nguyên chữ đúng như in trên giấy phép.
Không tự suy đoán, không sửa lỗi, không thêm thông tin không đọc được.
Trường không đọc được phải có giá trị chuỗi rỗng.

{
  "loai_giay_to": "Giấy phép lái xe",
  "so_giay_phep_lai_xe": "",
  "ho_va_ten": "",
  "ngay_sinh": "",
  "quoc_tich": "",
  "noi_cu_tru": "",
  "hang_giay_phep": "",
  "ngay_cap": "",
  "ngay_het_han": "",
  "noi_cap": ""
}
""".strip(),
    "Giấy đăng ký ô tô": """
Ảnh là Giấy đăng ký ô tô Việt Nam hoặc bản điện tử dược chụp qua điện thoại.

Trích xuất các trường đọc được:
- Số giấy đăng ký / Số chứng nhận
- Biển số
- Họ tên chủ xe
- Địa chỉ chủ xe
- Nhãn hiệu
- Số loại
- Loại xe
- Màu sơn
- Số máy
- Số khung
- Số chỗ ngồi
- Tự trọng
- Khối lượng hàng chuyên chở
- Ngày đăng ký
- Năm sản xuất
- Dung tích xi lanh
- Công suất
- Nguồn gốc

Quan trọng: chỉ lấy giá trị đứng đúng với từng nhãn. Không nhầm "Số:" với "Biển số".
Chỉ trả JSON hợp lệ. Giữ nguyên chữ đúng như in trên giấy đăng ký.
Không tự suy đoán, không sửa lỗi, không thêm thông tin không đọc được.
Trường không đọc được phải có giá trị chuỗi rỗng.

{
  "loai_giay_to": "Giấy đăng ký ô tô",
  "so_giay_dang_ky": "",
  "bien_so": "",
  "chu_xe": "",
  "dia_chi": "",
  "nhan_hieu": "",
  "so_loai": "",
  "loai_xe": "",
  "mau_son": "",
  "so_may": "",
  "so_khung": "",
  "so_cho_ngoi": "",
  "tu_trong": "",
  "khoi_luong_hang_cho_phep": "",
  "ngay_dang_ky": "",
  "nam_san_xuat": "",
  "dung_tich_xi_lanh": "",
  "cong_suat": "",
  "nguon_goc": ""
}
""".strip(),
    "Giấy đăng ký mô tô/xe máy": """
Ảnh là Giấy đăng ký mô tô/xe máy Việt Nam hoặc bản đăng kí điện tử.

Trích xuất các trường đọc được:
- Số giấy đăng ký / Số chứng nhận
- Biển số
- Họ tên chủ xe
- Địa chỉ chủ xe
- Nhãn hiệu
- Số loại
- Loại xe
- Màu sơn
- Số máy
- Số khung
- Dung tích xi lanh
- Ngày đăng ký
- Năm sản xuất
- Nguồn gốc

Quan trọng:
- "Số:" là số giấy đăng ký, không phải biển số.
- "Biển số:" là giá trị cần ghi vào trường "bien_so".
- Chỉ lấy giá trị đi cùng đúng nhãn in trên giấy.

Chỉ trả JSON hợp lệ. Giữ nguyên chữ đúng như hiển thị trên tài liệu
Không tự suy đoán, không sửa lỗi, không thêm thông tin không đọc được.
Trường không đọc được phải có giá trị chuỗi rỗng.

{
  "loai_giay_to": "Giấy đăng ký mô tô/xe máy",
  "so_giay_dang_ky": "",
  "bien_so": "",
  "chu_xe": "",
  "dia_chi": "",
  "nhan_hieu": "",
  "so_loai": "",
  "loai_xe": "",
  "mau_son": "",
  "so_may": "",
  "so_khung": "",
  "dung_tich_xi_lanh": "",
  "ngay_dang_ky": "",
  "nam_san_xuat": "",
  "nguon_goc": ""
}
""".strip(),
    "Đơn A4": """
Ảnh là một đơn hoặc biểu mẫu A4 tiếng Việt.

Hãy OCR và trích xuất các trường thông tin thực sự nhìn thấy trên văn bản.
Không dùng form cố định và không dồn toàn bộ nội dung vào một trường dài.

Yêu cầu:
- Tìm tiêu đề chính của đơn và trả vào "ten_don".
- Với mọi nhãn/trường thông tin nhìn thấy, tạo một phần tử riêng trong "fields".
- Mỗi phần tử gồm:
  - "label": tên nhãn/trường đúng như trên giấy.
  - "value": giá trị đi cùng nhãn đó.
- Nếu là bảng, mỗi dòng nhìn thấy là một phần tử riêng trong "tables".
- Nếu có đoạn văn không có nhãn cụ thể, đưa vào "noi_dung_khac" theo từng đoạn ngắn.
- Giữ nguyên tiếng Việt, số, dấu câu, xuống dòng khi đọc được.
- Không dịch, không sửa lỗi, không suy đoán, không tự điền phần bị mờ.
- Không gộp nhiều trường khác nhau vào cùng một value.
- Bỏ qua phần không đọc được.
- Chỉ trả JSON hợp lệ, không markdown.

Cấu trúc JSON:

{
  "loai_giay_to": "Đơn A4",
  "ten_don": "",
  "fields": [
    {
      "label": "",
      "value": ""
    }
  ],
  "tables": [
    {
      "table_name": "",
      "rows": [
        {
          "field_1": "",
          "field_2": ""
        }
      ]
    }
  ],
  "noi_dung_khac": [
    ""
  ]
}
""".strip(),
    "Ảnh xe ô tô": """
Ảnh là ảnh chụp xe ô tô thực tế ở Việt Nam.

Hãy tìm và đọc biển số xe trong ảnh.

Trích xuất các trường sau nếu nhìn thấy:
- Biển số xe (đọc chính xác ký tự trên biển)
- Màu xe (nếu nhìn rõ)
- Nhãn hiệu xe (nếu nhận diện được)

Chỉ trả JSON hợp lệ. Giữ nguyên đúng ký tự trên biển số.
Không tự suy đoán, không sửa lỗi.
Nếu không đọc được biển số, trường "bien_so" phải là chuỗi rỗng.

{
  "loai_giay_to": "Ảnh xe ô tô",
  "bien_so": "",
  "mau_xe": "",
  "nhan_hieu": ""
}
""".strip(),
    "Không xác định": """
Hình ảnh này có thể là bất kỳ tài liệu, giấy tờ, hoặc ảnh chụp nào.
Hãy tìm và trích xuất biển số xe (license plate / registration number) trong ảnh nếu có.
Nếu tìm thấy biển số xe, hãy trả về trong trường "bien_so".
Nếu không có biển số xe hoặc không đọc được, trường "bien_so" phải là chuỗi rỗng.

Chỉ trả JSON hợp lệ:
{
  "loai_giay_to": "Không xác định",
  "bien_so": ""
}
""".strip(),
}



# ── Helpers ────────────────────────────────────────────────────────────────────


def image_to_data_url(image_path: Path) -> str:
    """Encode an image file as a base64 data-URL string."""
    mime_type, _ = mimetypes.guess_type(image_path.name)
    if mime_type not in {"image/jpeg", "image/png", "image/webp", "image/gif"}:
        mime_type = "image/jpeg"
    encoded = base64.b64encode(image_path.read_bytes()).decode("utf-8")
    return f"data:{mime_type};base64,{encoded}"


def image_bytes_to_data_url(data: bytes, filename: str) -> str:
    """Encode raw image bytes as a base64 data-URL string."""
    mime_type, _ = mimetypes.guess_type(filename)
    if mime_type not in {"image/jpeg", "image/png", "image/webp", "image/gif"}:
        mime_type = "image/jpeg"
    encoded = base64.b64encode(data).decode("utf-8")
    return f"data:{mime_type};base64,{encoded}"


def _get_content(data: dict[str, Any]) -> str:
    """Extract text content from the LLM API response."""
    try:
        content = data["choices"][0]["message"]["content"]
    except (KeyError, IndexError, TypeError) as exc:
        raise RuntimeError(
            "API trả response không đúng định dạng:\n"
            + json.dumps(data, ensure_ascii=False, indent=2)
        ) from exc

    if isinstance(content, str):
        return content

    if isinstance(content, list):
        parts = []
        for item in content:
            if not isinstance(item, dict):
                continue
            if isinstance(item.get("text"), str):
                parts.append(item["text"])
            elif isinstance(item.get("content"), str):
                parts.append(item["content"])
        if parts:
            return "\n".join(parts)

    raise RuntimeError(f"Không đọc được message.content: {content!r}")


def _call_api(image_data_url: str, prompt: str) -> str:
    """Send a vision request to the LLM API and return raw text content."""
    payload: dict[str, Any] = {
        "model": MODEL,
        "temperature": 0,
        "messages": [
            {
                "role": "user",
                "content": [
                    {"type": "text", "text": prompt},
                    {"type": "image_url", "image_url": {"url": image_data_url}},
                ],
            }
        ],
    }

    try:
        response = requests.post(
            API_URL,
            headers={
                "Authorization": f"Bearer {API_KEY}",
                "Content-Type": "application/json",
            },
            json=payload,
            timeout=TIMEOUT_SECONDS,
        )
    except requests.RequestException as exc:
        raise RuntimeError(f"Không kết nối được API: {exc}") from exc

    if not response.ok:
        raise RuntimeError(f"API lỗi HTTP {response.status_code}:\n{response.text}")

    try:
        response_json = response.json()
    except ValueError as exc:
        raise RuntimeError(f"API không trả JSON:\n{response.text}") from exc

    return _get_content(response_json)


def parse_json(text: str) -> Any:
    """Parse JSON from LLM response, handling markdown fences."""
    text = text.strip()

    if text.startswith("```"):
        lines = text.splitlines()
        if lines and lines[0].strip().startswith("```"):
            lines = lines[1:]
        if lines and lines[-1].strip() == "```":
            lines = lines[:-1]
        text = "\n".join(lines).strip()

    try:
        return json.loads(text)
    except json.JSONDecodeError:
        match = re.search(r"\{[\s\S]*\}", text)
        if not match:
            raise
        return json.loads(match.group(0))


def _repair_json_text(text: str) -> str:
    repaired = text.strip()

    if repaired.startswith("```"):
        lines = repaired.splitlines()
        if lines and lines[0].strip().startswith("```"):
            lines = lines[1:]
        if lines and lines[-1].strip() == "```":
            lines = lines[:-1]
        repaired = "\n".join(lines).strip()

    match = re.search(r"\{[\s\S]*\}", repaired)
    if match:
        repaired = match.group(0)

    # Remove trailing commas before closing braces/brackets.
    repaired = re.sub(r",(\s*[}\]])", r"\1", repaired)

    # Escape raw newlines inside quoted strings conservatively.
    repaired = repaired.replace("\r\n", "\n")
    repaired = re.sub(r'":\s*"([^"]*)\n([^"]*)"', lambda m: '": "' + m.group(1) + "\\n" + m.group(2) + '"', repaired)

    return repaired


def parse_json_with_repair(text: str) -> Any:
    try:
        return parse_json(text)
    except json.JSONDecodeError:
        repaired = _repair_json_text(text)
        return parse_json(repaired)


# ── Public API ─────────────────────────────────────────────────────────────────


def classify_document(image_path: Path) -> dict[str, str]:
    """Classify the document type in an image.

    Returns a dict with either:
      {"loai_giay_to": "...", "ten_don": ""}  OR  {"ten_don": "..."}
    """
    data_url = image_to_data_url(image_path)
    raw_text = _call_api(data_url, CLASSIFICATION_PROMPT)
    parsed = parse_json(raw_text)

    if not isinstance(parsed, dict):
        raise ValueError("Kết quả phân loại phải là JSON object.")

    loai_tai_lieu = str(parsed.get("loai_tai_lieu", "Không xác định")).strip()
    ten_don = str(parsed.get("ten_don", "")).strip()

    if loai_tai_lieu not in VALID_TYPES:
        loai_tai_lieu = "Không xác định"

    return {"loai_giay_to": loai_tai_lieu, "ten_don": ten_don}


def detect_bbox(image_path: Path) -> dict[str, float] | None:
    """Detect document bounding box in the image.

    Returns {"x1", "y1", "x2", "y2"} in 0–1000 coordinates, or None on
    failure.
    """
    try:
        with Image.open(image_path) as img:
            w, h = img.size
        data_url = image_to_data_url(image_path)
        raw_text = _call_api(data_url, BBOX_PROMPT)
        parsed = parse_json(raw_text)

        if not isinstance(parsed, dict) or not isinstance(parsed.get("bbox"), dict):
            return None
        print(w)
        print(h)
        raw_bbox = parsed["bbox"]
        x1 = max(0.0, min(w, float(raw_bbox["x1"])))
        y1 = max(0.0, min(h, float(raw_bbox["y1"])))
        x2 = max(0.0, min(w, float(raw_bbox["x2"])))
        y2 = max(0.0, min(h, float(raw_bbox["y2"])))

        if x2 <= x1 or y2 <= y1:
            return None

        # Validate normalized values against the image dimensions.
        if not (0 <= x1 < x2 <= w and 0 <= y1 < y2 <= h ):
            return None

        return {
            "x1": round(x1, 2),
            "y1": round(y1, 2),
            "x2": round(x2, 2),
            "y2": round(y2, 2),
        }
    except Exception as e:
        logging.info("Error detecting bbox: %s", str(e))
        return None


def ocr_document(image_path: Path, document_type: str) -> dict[str, Any]:
    """OCR the document image using a type-specific prompt.

    Returns extracted data as a dict.
    """
    if document_type not in OCR_PROMPTS:
        raise ValueError(f"Không có prompt OCR cho loại: {document_type}")

    data_url = image_to_data_url(image_path)
    raw_text = _call_api(data_url, OCR_PROMPTS[document_type])
    try:
        parsed = parse_json_with_repair(raw_text)
    except Exception as exc:
        raise OCRParseError(f"OCR JSON parse failed: {exc}", raw_text) from exc

    if not isinstance(parsed, dict):
        raise OCRParseError("Kết quả OCR phải là JSON object.", raw_text)

    # Normalize: remove loai_giay_to, clean empty values
    result: dict[str, Any] = {"loai_giay_to": document_type}
    for key, value in parsed.items():
        if key == "loai_giay_to":
            continue
        if value is None:
            result[key] = ""
        elif isinstance(value, (str, int, float, bool)):
            result[key] = str(value).strip()
        else:
            result[key] = value

    return result
