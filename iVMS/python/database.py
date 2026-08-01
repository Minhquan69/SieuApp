"""SQLAlchemy database models and session management."""

from __future__ import annotations

import json
from datetime import datetime, timezone
from pathlib import Path

from sqlalchemy import (
    Column,
    DateTime,
    Enum,
    ForeignKey,
    Integer,
    String,
    Text,
    Boolean,
    create_engine,
    event,
    TypeDecorator,
)
from sqlalchemy.orm import (
    DeclarativeBase,
    Session,
    relationship,
    sessionmaker,
)

def _load_dotenv() -> None:
    import os
    from pathlib import Path
    paths_to_try = [
        Path(__file__).resolve().parent.parent / ".env",
        Path(__file__).resolve().parent / ".env",
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

DB_PATH = Path(__file__).resolve().parent / "data" / "app.db"
DB_PATH.parent.mkdir(parents=True, exist_ok=True)

DATABASE_URL = os.getenv("DATABASE_URL", f"sqlite:///{DB_PATH}")

ENGINE = create_engine(
    DATABASE_URL,
    connect_args={"check_same_thread": False} if DATABASE_URL.startswith("sqlite") else {},
    echo=False,
)


# Enable WAL mode and foreign keys for SQLite
@event.listens_for(ENGINE, "connect")
def _set_sqlite_pragma(dbapi_connection, _connection_record):
    cursor = dbapi_connection.cursor()
    try:
        cursor.execute("PRAGMA journal_mode=WAL")
    except Exception:
        try:
            cursor.execute("PRAGMA journal_mode=delete")
        except Exception:
            pass
    cursor.execute("PRAGMA foreign_keys=ON")
    cursor.close()


SessionLocal = sessionmaker(bind=ENGINE, autocommit=False, autoflush=False)


class UTCDateTime(TypeDecorator):
    """DateTime type that ensures UTC timezone is attached and stored in SQLite."""
    impl = DateTime
    cache_ok = True

    def process_bind_param(self, value, dialect):
        if value is not None:
            if value.tzinfo is None:
                value = value.replace(tzinfo=timezone.utc)
            else:
                value = value.astimezone(timezone.utc)
        return value

    def process_result_value(self, value, dialect):
        if value is not None:
            if value.tzinfo is None:
                value = value.replace(tzinfo=timezone.utc)
        return value


class Base(DeclarativeBase):
    pass


class Record(Base):
    """Một hồ sơ đăng ký xe (một lượt tiếp nhận từ bodycam)."""

    __tablename__ = "records"

    id = Column(Integer, primary_key=True, autoincrement=True)
    title = Column(String(255), nullable=True, default="")
    status = Column(
        Enum("pending", "processing", "done", name="record_status"),
        nullable=False,
        default="pending",
    )
    confirmed = Column(Boolean, nullable=False, default=False)
    created_at = Column(
        UTCDateTime, nullable=False, default=lambda: datetime.now(timezone.utc)
    )
    updated_at = Column(
        UTCDateTime,
        nullable=False,
        default=lambda: datetime.now(timezone.utc),
        onupdate=lambda: datetime.now(timezone.utc),
    )

    images = relationship(
        "Image", back_populates="record", cascade="all, delete-orphan"
    )

    def to_dict(self) -> dict:
        created_str = None
        if self.created_at:
            dt = self.created_at
            if not dt.tzinfo:
                dt = dt.replace(tzinfo=timezone.utc)
            created_str = dt.isoformat().replace("+00:00", "Z")

        updated_str = None
        if self.updated_at:
            dt = self.updated_at
            if not dt.tzinfo:
                dt = dt.replace(tzinfo=timezone.utc)
            updated_str = dt.isoformat().replace("+00:00", "Z")

        return {
            "id": self.id,
            "title": self.title or "",
            "status": self.status,
            "confirmed": self.confirmed,
            "created_at": created_str,
            "updated_at": updated_str,
            "images": [img.to_dict() for img in self.images],
        }


class Image(Base):
    """Một ảnh giấy tờ trong hồ sơ."""

    __tablename__ = "images"

    id = Column(Integer, primary_key=True, autoincrement=True)
    record_id = Column(
        Integer, ForeignKey("records.id", ondelete="CASCADE"), nullable=False
    )
    original_filename = Column(String(512), nullable=False)
    original_path = Column(String(1024), nullable=False)
    cropped_path = Column(String(1024), nullable=True)
    document_type = Column(String(255), nullable=True)
    document_name = Column(String(512), nullable=True)
    ocr_data = Column(Text, nullable=True)  # JSON string
    status = Column(
        Enum("pending", "processing", "done", "error", name="image_status"),
        nullable=False,
        default="pending",
    )
    error_message = Column(Text, nullable=True)
    created_at = Column(
        UTCDateTime, nullable=False, default=lambda: datetime.now(timezone.utc)
    )
    updated_at = Column(
        UTCDateTime,
        nullable=False,
        default=lambda: datetime.now(timezone.utc),
        onupdate=lambda: datetime.now(timezone.utc),
    )

    record = relationship("Record", back_populates="images")

    def to_dict(self) -> dict:
        ocr = None
        if self.ocr_data:
            try:
                ocr = json.loads(self.ocr_data)
            except (json.JSONDecodeError, TypeError):
                ocr = self.ocr_data

        created_str = None
        if self.created_at:
            dt = self.created_at
            if not dt.tzinfo:
                dt = dt.replace(tzinfo=timezone.utc)
            created_str = dt.isoformat().replace("+00:00", "Z")

        updated_str = None
        if self.updated_at:
            dt = self.updated_at
            if not dt.tzinfo:
                dt = dt.replace(tzinfo=timezone.utc)
            updated_str = dt.isoformat().replace("+00:00", "Z")

        return {
            "id": self.id,
            "record_id": self.record_id,
            "original_filename": self.original_filename,
            "original_path": self.original_path,
            "cropped_path": self.cropped_path,
            "document_type": self.document_type,
            "document_name": self.document_name,
            "ocr_data": ocr,
            "status": self.status,
            "error_message": self.error_message,
            "created_str": created_str, # Keep both created_str and created_at keys for compatibility
            "created_at": created_str,
            "updated_at": updated_str,
        }


def init_db() -> None:
    """Create all tables if they don't exist."""
    Base.metadata.create_all(bind=ENGINE)


def get_db():
    """FastAPI dependency — yields a DB session and closes it after use."""
    db = SessionLocal()
    try:
        yield db
    finally:
        db.close()
