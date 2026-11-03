# chatbot — Instagram/Telegram AI sales agent

Copied on 2026-10-30 from `WunderkindAIAgent` commit `5d6e495` (its own repo, still the source of the
learning-centre copy on the old droplet). Inside LMS it runs as three containers behind the
`chatbot` compose profile; the panel opens from the LMS menu "Chatbot". Design and cut-over:
`docs/modules/chatbot.md`. The original manual and product spec: `chatbot/docs/`.

Changes against 5d6e495 (all additive):
- panel under a path prefix (`VITE_BASE`, `Caddyfile.lms`), API calls through `API_BASE`;
- `POST /api/auth/sso` — the one-time token from LMS (`LMS_SSO_KEY`), `tests/test_lms_sso.py`;
- Instagram connect leaves through the top window (Instagram refuses to be framed);
- inside LMS the own sidebar is hidden and the route follows the LMS submenus (`components/LmsBridge.tsx`).

Tests: `docker run --rm -v "$PWD/chatbot/backend":/app -w /app python:3.12-slim sh -c 'pip install -q -r requirements-dev.txt && python -m pytest -q'`
