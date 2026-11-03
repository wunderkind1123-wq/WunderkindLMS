"""Login and current user."""
from __future__ import annotations

import secrets

import jwt
from fastapi import APIRouter, HTTPException, Request, status
from sqlalchemy import select

from app.config import settings
from app.core.deps import DB, CurrentUser
from app.core.security import create_token, hash_password, verify_password
from app.models.user import User
from app.schemas.api import LoginIn, LoginOut, SsoIn, UserOut
from app.state.store import store

router = APIRouter(prefix="/auth", tags=["Auth"])


_LOGIN_WINDOW = 15 * 60
_LOGIN_MAX_ATTEMPTS = 10


@router.post("/login", response_model=LoginOut)
async def login(payload: LoginIn, db: DB, request: Request):
    ip = request.client.host if request.client else "?"
    key = f"login:{ip}:{payload.username.strip().lower()}"
    if await store.bump_rate(key, _LOGIN_WINDOW) > _LOGIN_MAX_ATTEMPTS:
        raise HTTPException(status.HTTP_429_TOO_MANY_REQUESTS,
                            "Juda ko'p urinish. 15 daqiqadan keyin qayta urinib ko'ring")
    user = (await db.execute(
        select(User).where(User.username == payload.username.strip())
    )).scalar_one_or_none()
    if not user or not user.is_active or not verify_password(payload.password, user.password_hash):
        raise HTTPException(status.HTTP_401_UNAUTHORIZED, "Login yoki parol noto'g'ri")
    return LoginOut(access_token=create_token(str(user.id)), user=UserOut.model_validate(user))


_SSO_ROLES = {"admin", "operator"}


@router.post("/sso", response_model=LoginOut)
async def sso(payload: SsoIn, db: DB):
    """One-time token from WunderkindLMS, which embeds this panel (its docs/modules/chatbot.md §3).

    LMS signs ``{sub, name, role, purpose: "lms-sso", jti, iss: "SchoolLms", exp}`` with the shared
    LMS_SSO_KEY; each ``jti`` works once. The user is ``lms:<sub>`` and takes the role from the token,
    so a role change in LMS applies the next time the panel is opened there.
    """
    if not settings.LMS_SSO_KEY:
        raise HTTPException(status.HTTP_404_NOT_FOUND, "Not Found")
    try:
        data = jwt.decode(payload.token, settings.LMS_SSO_KEY, algorithms=["HS256"],
                          issuer="SchoolLms", options={"require": ["exp", "sub", "jti"]})
    except jwt.PyJWTError:
        raise HTTPException(status.HTTP_401_UNAUTHORIZED, "Kirish havolasi eskirgan, LMS'dan qayta oching") from None
    role = data.get("role")
    if data.get("purpose") != "lms-sso" or role not in _SSO_ROLES:
        raise HTTPException(status.HTTP_401_UNAUTHORIZED, "Token noto'g'ri")
    if await store.bump_rate(f"sso:{data['jti']}", 600) > 1:
        raise HTTPException(status.HTTP_401_UNAUTHORIZED, "Kirish havolasi ishlatilgan, LMS'dan qayta oching")

    username = f"lms:{data['sub']}"[:64]
    user = (await db.execute(select(User).where(User.username == username))).scalar_one_or_none()
    if user is None:
        # No password login for these accounts: a random hash nobody knows.
        user = User(username=username, password_hash=hash_password(secrets.token_urlsafe(32)), is_active=True)
        db.add(user)
    elif not user.is_active:
        raise HTTPException(status.HTTP_403_FORBIDDEN, "Foydalanuvchi o'chirilgan")
    user.full_name = str(data.get("name") or "")[:255]
    user.role = role
    await db.commit()
    await db.refresh(user)
    return LoginOut(access_token=create_token(str(user.id)), user=UserOut.model_validate(user))


@router.get("/me", response_model=UserOut)
async def me(user: CurrentUser):
    return user
