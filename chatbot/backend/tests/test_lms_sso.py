"""LMS single sign-on: POST /api/auth/sso (WunderkindLMS docs/modules/chatbot.md §3)."""
from __future__ import annotations

import uuid
from datetime import datetime, timedelta, timezone

import jwt
import pytest

KEY = "lms-sso-test-key-that-is-long-enough-123456"


def _token(*, role="admin", sub="user-1", name="Ali Valiyev", key=KEY, minutes=2,
           purpose="lms-sso", issuer="SchoolLms", jti=None) -> str:
    now = datetime.now(timezone.utc)
    return jwt.encode({"sub": sub, "name": name, "role": role, "purpose": purpose, "iss": issuer,
                       "jti": jti or uuid.uuid4().hex, "iat": now, "exp": now + timedelta(minutes=minutes)},
                      key, algorithm="HS256")


@pytest.fixture
def sso_on(monkeypatch):
    from app.config import settings

    monkeypatch.setattr(settings, "LMS_SSO_KEY", KEY)


def test_sso_is_off_without_a_key(client):
    assert client.post("/api/auth/sso", json={"token": _token()}).status_code == 404


def test_sso_creates_the_user_and_the_session_works(client, sso_on):
    r = client.post("/api/auth/sso", json={"token": _token(role="operator")})
    assert r.status_code == 200, r.text
    body = r.json()
    assert body["user"]["username"] == "lms:user-1"
    assert body["user"]["role"] == "operator"
    assert body["user"]["full_name"] == "Ali Valiyev"

    me = client.get("/api/auth/me", headers={"Authorization": f"Bearer {body['access_token']}"})
    assert me.status_code == 200
    # An operator is not an admin here either.
    users = client.get("/api/users", headers={"Authorization": f"Bearer {body['access_token']}"})
    assert users.status_code == 403


def test_sso_takes_the_role_from_lms_each_time(client, sso_on):
    assert client.post("/api/auth/sso", json={"token": _token(role="operator")}).json()["user"]["role"] == "operator"
    again = client.post("/api/auth/sso", json={"token": _token(role="admin")}).json()
    assert again["user"]["role"] == "admin"
    assert again["user"]["username"] == "lms:user-1"


def test_sso_token_works_once(client, sso_on):
    token = _token()
    assert client.post("/api/auth/sso", json={"token": token}).status_code == 200
    assert client.post("/api/auth/sso", json={"token": token}).status_code == 401


@pytest.mark.parametrize("kwargs", [
    {"key": "another-key-that-is-long-enough-1234567890"},
    {"minutes": -1},
    {"purpose": "access"},
    {"issuer": "Somebody"},
    {"role": "superadmin"},
])
def test_sso_refuses_a_bad_token(client, sso_on, kwargs):
    assert client.post("/api/auth/sso", json={"token": _token(**kwargs)}).status_code == 401


def test_sso_refuses_a_deactivated_user(client, sso_on, database):
    import asyncio

    from sqlalchemy import update

    from app.models.user import User

    assert client.post("/api/auth/sso", json={"token": _token()}).status_code == 200

    async def _off():
        async with database() as s:
            await s.execute(update(User).where(User.username == "lms:user-1").values(is_active=False))
            await s.commit()

    asyncio.run(_off())
    assert client.post("/api/auth/sso", json={"token": _token()}).status_code == 403
