# Hướng dẫn kỹ thuật: Bố cục tùy chỉnh ở trang Trực tiếp

## Mục đích

Tính năng cho phép tạo lưới camera theo số hàng và cột tùy ý, sau đó gộp hoặc tách các ô để tạo khung camera lớn/nhỏ.

## Thành phần mã nguồn

| File | Trách nhiệm |
|---|---|
| iVMS/UI/Views/LivePage_v3.xaml | Menu bố cục và nút “Bố cục tùy chỉnh...”. |
| iVMS/UI/Views/LivePage_v3.xaml.cs | Nhận kết quả, lưu cấu hình và dựng lưới camera. |
| iVMS/ucs/CustomLayoutDialog_v3.xaml | UI chọn hàng/cột, xem trước, gộp/tách và áp dụng. |
| iVMS/ucs/CustomLayoutDialog_v3.xaml.cs | Logic chọn vùng, kiểm tra, gộp/tách ô. |
| iVMS/viewModels/LiveViewModel_v3.cs | Tạo số slot camera theo số khung hiển thị. |

## Luồng sử dụng

    Trang Trực tiếp
      → Menu Bố cục
      → “Bố cục tùy chỉnh...”
      → Nhập số hàng / số cột
      → “Tạo lại lưới”
      → Kéo chuột chọn vùng hình chữ nhật
      → Gộp vùng chọn hoặc tách ô
      → “Áp dụng”
      → BuildGrid dựng các tile camera

## UI hộp chọn

| Thành phần | Chức năng |
|---|---|
| Số hàng, Số cột | Nhập kích thước lưới, chỉ nhận số nguyên dương. |
| Tạo lại lưới | Tạo lại toàn bộ ô đơn 1 × 1; bỏ các vùng đã gộp. |
| PreviewGrid | Bảng xem trước; giữ và kéo chuột để chọn vùng. |
| Gộp vùng chọn | Gộp các ô trong vùng chọn thành một khung lớn. |
| Tách ô | Tách khung đã gộp thành các ô đơn. |
| Áp dụng | Xác nhận và trả cấu hình về LivePage_v3. |

Ô đã gộp hiển thị kích thước như 2×2; vùng đang chọn có nền và viền xanh.

## Cấu trúc dữ liệu

    public sealed class CustomLayoutCell_v3
    {
        public int Row { get; set; }
        public int Column { get; set; }
        public int RowSpan { get; set; } = 1;
        public int ColumnSpan { get; set; } = 1;
    }

Row và Column là tọa độ bắt đầu, tính từ 0. RowSpan và ColumnSpan là số hàng/cột mà khung chiếm.

Ví dụ khung 2×2 góc trên-trái:

    Row = 0, Column = 0, RowSpan = 2, ColumnSpan = 2

## Tạo lại lưới

1. Đọc số hàng và số cột.
2. Kiểm tra hợp lệ.
3. Xóa cấu hình ô cũ.
4. Tạo một CustomLayoutCell_v3 1×1 cho từng vị trí.
5. Xóa vùng chọn và render lại PreviewGrid.

## Chọn và gộp vùng

Người dùng kéo chuột giữa hai ô. Hệ thống chuẩn hóa vùng thành:

    minRow, minColumn, maxRow, maxColumn

Khi gộp:

1. Kiểm tra có vùng chọn.
2. Lấy mọi khung nằm trọn trong vùng.
3. Xác nhận các khung phủ kín một hình chữ nhật liên tục.
4. Xóa các khung cũ.
5. Tạo một khung mới với RowSpan và ColumnSpan tương ứng.
6. Render lại preview.

Không thể gộp vùng có ô khuyết, chéo hoặc chồng lấn.

## Tách ô

1. Tìm khung đã gộp trong vùng chọn.
2. Xóa khung lớn.
3. Tạo lại toàn bộ ô 1×1 trong phạm vi khung đó.
4. Render lại preview.

## Ràng buộc

- Số hàng và cột lớn hơn 0.
- Tổng số ô nền không vượt quá 120.
- Nếu sửa số hàng/cột, phải nhấn “Tạo lại lưới” trước khi áp dụng.
- Chỉ hỗ trợ vùng gộp hình chữ nhật liên tục.
- LayoutCells trả về bản sao để bên ngoài không sửa trực tiếp dữ liệu nội bộ.

## Áp dụng vào lưới camera

Sau khi người dùng nhấn Áp dụng, LivePage_v3 nhận:

    dialog.Rows
    dialog.Columns
    dialog.LayoutCells

Sau đó lưu vào _customLayoutRows, _customLayoutColumns, _customLayoutCells; gọi:

    _viewModel.ApplyCustomLayout(_customLayoutCells.Count);
    BuildGrid(deferStaleCleanup: true);

Số slot camera bằng số khung hiển thị **sau khi gộp**, không phải số ô nền. Ví dụ lưới 3×3 gộp một vùng 2×2 sẽ có 6 khung hiển thị, nên tạo 6 slot camera.

## Dựng tile camera

BuildGrid đặt từng tile theo CustomLayoutCell_v3:

    Grid.SetRow(tile, cell.Row);
    Grid.SetColumn(tile, cell.Column);
    Grid.SetRowSpan(tile, cell.RowSpan);
    Grid.SetColumnSpan(tile, cell.ColumnSpan);

Do đó các camera được gán tuần tự vào từng khung hiển thị sau khi gộp.

## Lưu ý bảo trì

- Không đổi thứ tự _customLayoutCells nếu không muốn đổi thứ tự camera.
- Khi chuyển sang layout có sẵn như 1×1, 2×2, 3×3 hoặc 5+1, cấu hình custom phải được xóa hoặc bỏ qua.
- Không bỏ sự kiện OpenCustomLayoutEditor_Click khỏi menu bố cục đang hiển thị, nếu không hộp thoại vẫn tồn tại nhưng người dùng không thể mở.
