# Olimpiada (mock imtihonlar) — alohida bot va CRM bo'limi

Mijoz TZ'si: 2026-11-16 (`maktab-mock-bot-TZ.md`). Maktab tashqaridan kelgan bolalar uchun **bepul olimpiada
mock imtihonlari** o'tkazadi. Ro'yxatdan o'tish **alohida Telegram bot** orqali (maktab botidan boshqa token),
CRM'da — "Olimpiada" bo'limi.

## Qisqacha oqim

1. Ota-ona botga `/start` bosadi → majburiy kanallarga obuna → menyu.
2. **Ro'yxatdan o'tish**: ochiq tadbir → nizom ("✅ Nizom bilan tanishib chiqdim") → saqlangan bola yoki
   yangi bola (F.I.Sh, telefon, sinf, maktab) → tasdiqlash → raqam (`M{kod}-0001`) va **QR kod** rasmi.
   Joy yo'q bo'lsa — kutish ro'yxati (yoki "joylar tugadi").
3. Eslatmalar (standart: 1 kun va 3 soat oldin + qo'shimchalar).
4. Imtihon kuni — **Kirish** sahifasi: telefon kamerasi bilan QR skan yoki qidiruv. "Yakunlash" →
   belgilanmaganlar `no_show`.
5. Natijalar: qo'lda yoki Excel (shablon → oldindan ko'rish → tasdiqlash). Foiz va o'rin (1, 2, 2, 4).
6. "Natijalarni yuborish" → navbat orqali har kelgan bolaga natija + sertifikat (+ "Maktabga taklif" tugmasi).
7. Qiziqqanlar → **Lidga aylantirish** (Lidlar moduliga manba `olympiad`).
8. Rassilka: auditoriya filtri, formatlangan matn, rasm/albom/fayl/video/dumaloq video, URL tugmalar,
   test yuborish, rejalashtirish, hisobot.

## Arxitektura

| Qism | Fayl |
|---|---|
| Entity'lar | `SchoolLms.Domain/Olympiad.cs` |
| Jadval sozlamasi | `SchoolLms.Infrastructure/Data/OlympiadModel.cs`, migratsiya `AddOlympiad` |
| Bot (long polling + FSM) | `SchoolLms.Application/Olympiad/OlympiadBotService.cs` |
| Telegram mijozi (alohida token) | `OlympiadTelegram.cs` |
| Xabar yig'ish (caption/albom/limitlar, file_id keshi) | `OlympiadMessenger.cs` |
| Ro'yxat, kutish ro'yxati, QR xabari | `OlympiadRegistrar.cs` |
| Natija, import, sertifikat | `OlympiadResults.cs`, `OlympiadDocuments.cs` (QuestPDF) |
| Navbat: eslatma, rassilka, natija | `OlympiadDispatcher.cs` (`OlympiadScheduler`, har 20 s) |
| Matnlar, shablonlar, tekshiruvlar | `OlympiadText.cs` |
| API | `SchoolLms.Server/Controllers/Olympiad*Controller.cs` |
| CRM sahifalari | `schoollms.client/src/pages/admin/olympiad/` |

Barcha jadvallar `olympiad_` prefiksli. Mavjud jadvallarga yagona o'zgarish — `leads.source` cheklovi
(`ck_leads_source`) ga `olympiad` qiymati qo'shildi va voronkada "Olimpiada" manbasi chiqadi.

## Ruxsatlar

| Kalit | Nima beradi |
|---|---|
| `olympiad` | Butun bo'lim (tadbirlar, ishtirokchilar, natijalar, rassilka, sozlamalar) |
| `olympiad:view` | Faqat ko'rish |
| `olympiadCheckin` | Faqat **Kirish** sahifasi (QR skaner, qidiruv, yakunlash) |

Admin va superadmin — hammasi. O'qish ham himoyalangan (`GatedRead`): bolalarning telefonlari bor.
Ruxsat Boshqaruv → Rollar oynasida beriladi.

## TZ'dan farqlar (qabul qilingan qarorlar)

1. **Ruxsatlar** loyihaning mavjud rol tizimida (TZ'dagi `mock.view/manage/checkin/grant` va alohida
   "Ruxsatlar" sahifasi o'rniga). "Direktor" roli loyihada yo'q — admin/superadmin.
2. **Jadval prefiksi** `olympiad_` (bo'lim nomi "Olimpiada").
3. **Webhook emas, long polling** — maktab boti ham shunday ishlaydi; tashqi manzil va Cloudflare
   sozlamasi kerak emas. Token o'zgarsa bot o'zi qayta ulanadi.
4. **Token** maktab botidagi kabi bazada saqlanadi; API uni hech qachon to'liq qaytarmaydi (niqoblangan).
   Maktab botining tokenini kiritib bo'lmaydi (ikki xizmat bir-birining yangilanishlarini "yeb qo'yardi").
5. **Natija** alohida jadval emas — `olympiad_registrations` qatoridagi ustunlar (bitta yozuv = bitta natija).
6. **Lidga aylantirish**: telefon (oxirgi 9 raqam) lidlarda bo'lsa — yangi lid yaratilmaydi, bola mavjud lidga
   bog'lanadi va xabar qaytadi. Lidda: bola ismi, ota-ona (Telegram ismi), telefon, sinf, izohda maktab,
   tadbir va natija. `Gender` — standart qiymat (anketada so'ralmaydi).
7. **"Menga test yuborish"** xodimning maktab botida ulangan Telegram akkauntiga olimpiada boti orqali
   yuboriladi — xodim avval olimpiada botiga `/start` bosgan bo'lishi kerak.
8. Bola ma'lumotini ota-ona botda tadbir boshlanguncha va natija yuborilguncha tahrirlaydi; keyin — faqat CRM.

## 2-bosqichga qoldirilgan

- **Sertifikat shabloni yuklash va maydonlarni sichqoncha bilan joylash muharriri.** Hozir — standart
  dizayn (A4 landshaft, logotip, ramka, "SERTIFIKAT", ism katta harfda, ball, o'rin, sana, imzo joyi),
  PDF yoki PNG.
- O'rinni sinf bo'yicha hisoblash (TZ'da ixtiyoriy).

## Sinov ro'yxati (TZ §16)

`SchoolLms.Tests/OlympiadRulesTests.cs` (sof qoidalar) va `OlympiadApiTests.cs` (ruxsatlar, limit va kutish
ro'yxati, 12 ta parallel yozilishda limitdan oshmaslik, QR kirish, o'rinlar, lidga aylantirish).
Botning Telegram bilan ishlashi — qo'lda: test bot tokeni bilan lokal muhitda.
