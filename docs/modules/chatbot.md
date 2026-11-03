# Chatbot — the Instagram/Telegram AI sales agent inside LMS

Status: SPEC 2026-10-30. Client: *"chatbot qilgandigu instagramga avtomatik javob beradigan qilib shuni bu
tizimga qo'shib yuborish kerak, bitta alohida menu sifatida bo'lishi kerak … topshiriqlar projectni bu projectga
qo'shib yuborgandigu shunga o'xshab chatbotni ham"*. Path chosen by the user (2026-10-30): **side-by-side
service** — the working Python code moves into this repository and this server unchanged, LMS gets a "Chatbot"
menu with one login, and later the chatbot's leads land in Lidlar. Rewriting it in C# was offered (3–4 weeks)
and declined.

## 1. What exists today

`~/Documents/Projects/WunderkindAIAgent` (commit 5d6e495): FastAPI + SQLAlchemy/Alembic (10 migrations) +
APScheduler, React/Vite admin panel served by Caddy. ~15k lines Python, ~8k TS. It answers Instagram DMs and
comments and Telegram chats with Gemini, runs lead-magnet funnels, public forms (`/f/<slug>`), Meta legal pages
and callbacks, and alerts staff in Telegram. Its own users (admin | operator) and JWT.

Live on a DigitalOcean droplet `root@64.225.109.187`, `/opt/wunderkind-agent`:
- `wkagent` = the school — https://chatbot.wunderkindedu.uz (188 leads, 843 messages on 2026-10-30, 14 MB DB).
- `brand2-agent` = **WunderkindLC (the learning centre)** — https://markazchatbot.wunderkindedu.uz, behind the
  `wkagent-web` Caddy. **Out of scope** (centre, not school): it stays on the droplet untouched.

Secrets in its DB (AI keys, Instagram tokens, Telegram tokens) are encrypted with `SECRET_KEY` — the server's
`.env` value must move with the data or every stored secret becomes unreadable.

## 2. Target shape

```
lms.wunderkindedu.uz/admin/chatbot/*    LMS page: menu "Chatbot" with the panel's pages as submenus, one iframe of /chatbot/
lms.wunderkindedu.uz/chatbot/*          chatbot panel (SPA built with base /chatbot/) and /chatbot/api/*
chatbot.wunderkindedu.uz/*              webhooks, OAuth callback, legal pages, /f/ forms, /go/ — unchanged URLs
```

- Code: `chatbot/backend`, `chatbot/frontend` in this repo (copied from 5d6e495; `chatbot/README.md` notes the
  origin). Changes to it are additive and listed in §4.
- Containers (in `docker-compose.yml`, profile `chatbot` so a plain `up` never starts it by accident):
  `chatbot-backend` (python, 1 worker — in-process scheduler), `chatbot-web` (its Caddy: SPA + proxy to
  chatbot-backend), `chatbot-redis` (own Redis — the LMS cache is `allkeys-lru` and would evict chatbot state).
- Database: a separate database `wk_agent` with its own login role `wk_agent` **in the LMS PostgreSQL
  container** (no second Postgres; nothing shared with `schoollms`). The daily backup container dumps it too.
- LMS Caddy (`deploy/Caddyfile`): `chatbot.wunderkindedu.uz` → `chatbot-web:80`; on the LMS host
  `/chatbot/*` → `chatbot-web:80`.
- Memory: ~250 MB more on a server with ~2.5 GB available.

## 3. One login (SSO)

LMS never shares its JWT key with the chatbot. Instead:

1. LMS page calls `POST /api/admin/chatbot/sso` — `[AdminPerm("chatbot", GatedRead = true)]`, so staff need the
   new `chatbot` key; admin/superadmin always pass; teachers/parents 403.
2. LMS signs a 2-minute HS256 token with `Chatbot__SsoKey` (env `CHATBOT_SSO_KEY`, also given to the chatbot):
   `{ sub: lms user id, name, role: admin | operator, purpose: "lms-sso", jti, iss: "SchoolLms" }`.
   Role: admin/superadmin or staff with full `chatbot` → `admin`; staff with `chatbot:view` → `operator`.
   No key configured → 503 "Chatbot ulanmagan".
3. The iframe opens `/chatbot/#sso=<token>`; the panel posts it to `POST /chatbot/api/auth/sso`, which verifies
   signature, purpose, expiry and single use (`jti` in Redis), upserts the chatbot user `lms:<id>` (role from
   the token, so a role change in LMS applies on the next open) and returns the chatbot's normal access token.
4. The chatbot's own login form stays (break-glass admin from `ADMIN_USERNAME`).

## 4. Changes to the chatbot code (additive)

- Frontend (2026-11-03, client: chatbot menus as LMS submenus): inside a frame the panel hides its own
  sidebar and follows `wk-chatbot:navigate` messages from the same-origin parent; it reports every route as
  `wk-chatbot:location` so `/admin/chatbot/<page>` stays in step (`components/LmsBridge.tsx`, LMS `ChatbotPage`).
  Opened directly on the chatbot domain it looks as before.
- Frontend: Vite `base` and router `basename` from `VITE_BASE` (default `/`, LMS build `/chatbot/`); axios
  `baseURL` = `${base}api`; SSO bootstrap from `#sso=`; the hash is removed from the address bar at once.
- Backend: `app/api/auth.py` gets `POST /auth/sso` (LMS_SSO_KEY empty → 404, the endpoint does not exist).
- Caddy (frontend/Caddyfile): with `BASE_PATH=/chatbot` the SPA and `/api` are served under that prefix; the
  public routes (`/webhook`, `/connect`, `/privacy`, `/terms`, `/data-deletion`, `/go`, `/f`, `/health`) stay at
  the root for the chatbot domain. Root `/` of the chatbot domain redirects to the LMS menu.

## 5. Cut-over (once; one backend at a time)

Two backends with the same Telegram token and scheduler would answer customers twice and send the daily
report / follow-ups twice — so the old one stops before the new one starts.

1. Deploy LMS with the chatbot services built but the profile off. Create role + database `wk_agent`.
2. Droplet: `pg_dump` wkagent (backup kept on the droplet and copied here), **stop `wkagent-backend` only**
   (`wkagent-web` keeps serving markazchatbot; `wkagent-db` stays for rollback; restart policy → no).
3. Restore the dump into `wk_agent`, carry `SECRET_KEY`, `PUBLIC_URL`, `ADMIN_*`, `TIMEZONE` into LMS `.env`
   (`CHATBOT_*`), start the profile; `alembic upgrade head` is a no-op on the same revision.
4. **User**: Cloudflare DNS `chatbot.wunderkindedu.uz` → `91.99.187.196` (proxied). Until then webhooks hit the
   stopped droplet backend; Meta and Telegram retry, nothing is lost, replies are only late.
5. Check: `/health`, panel through LMS, a test DM, Telegram bot answer, the scheduler log.

Rollback: stop the LMS chatbot profile, start `wkagent-backend` on the droplet, DNS back.

## 6. Phase B — chatbot leads in Lidlar (after the cut-over works)

A chatbot lead with a phone becomes an LMS lead: `leads.source = 'chatbot'` + `chatbot_lead_id` (unique) so a
re-send updates instead of duplicating; first stage of the board; name/phone/grade/note from the chatbot.
Push from the chatbot to an internal LMS endpoint signed with the same `CHATBOT_SSO_KEY`. The protected board
files are not touched (data only). Needs an LMS migration — specified separately when phase A is live.

## 7. Not in scope

- The learning-centre bot (markazchatbot) — centre project.
- Rewriting the chatbot in C# / merging its tables into `schoollms`.
- Restyling the chatbot panel to the LMS look (it keeps its own UI inside the iframe).
