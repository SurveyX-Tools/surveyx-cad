# SurveyX CAD

أدوات مساحة **مجانية** للأوتوكاد وسيفيل ثري دي (من إصدار 2021 لحد 2026). تطوير: **Ahmed Gamal Shalaby**.

Free surveying tools for **AutoCAD & Civil 3D 2021–2026** — by Ahmed Gamal Shalaby.

## التثبيت / Install
1. حمّل `SurveyXCAD_Setup_x.y.z.exe` من صفحة [Releases](../../releases/latest).
2. اقفل الأوتوكاد وشغّل ملف التثبيت (مش محتاج صلاحيات أدمن).
3. افتح الأوتوكاد أو سيفيل ثري دي، وهتلاقي تبويب **SurveyX** في الشريط، أو اكتب `SXHELP`.

## الأوامر / Commands
| الأمر | الوظيفة |
|---|---|
| `SXIMPORT` | استيراد النقط (CSV, TXT, Excel, LandXML, KML, GeoJSON) مع المعاينة والفحص |
| `SXEXPORT` | تصدير النقط |
| `SXCONVERT` | تحويل الإحداثيات (نقطة / ملف / رسمة) |
| `SXTABLE` | جدول إحداثيات |
| `SXLABEL` | ليدر بالشرقي والشمالي والمنسوب |
| `SXPOINT` | إضافة نقط بالضغط |
| `SXSETTINGS` | الإعدادات (اللغة، ارتفاع الكتابة، الأرقام العشرية، الطبقات) |

## أنظمة الإحداثيات / Coordinate systems
WGS 84 · UTM 35N–40N · DLTM (EPSG:3997) · Nahrwan 1967 / UTM 39N, 40N (UAE, Abu Dhabi 7-param) · Egypt 1907 Red / Purple / Extended Purple / Blue belts.
الحسابات اتراجعت قصاد مكتبة PROJ الرسمية، والفرق أقل من 2 مم (ده بيتختبر تلقائيًا مع كل نسخة).

## للمطورين / Building
Every push is built on GitHub Actions (Windows): unit tests → `net48` + `net8.0-windows` builds → AutoCAD bundle → Inno Setup installer.
Push a tag `vX.Y.Z` to publish a release.
