using System.Collections.Generic;

namespace SurveyX.Core
{
    /// <summary>Arabic / English strings. Missing keys fall back to English, then to the key itself.</summary>
    public static class Lang
    {
        public static bool IsArabic { get; private set; } = true;
        public static void Set(string code) => IsArabic = code != "en";

        public static string T(string key)
        {
            var d = IsArabic ? Ar : En;
            if (d.TryGetValue(key, out var v)) return v;
            return En.TryGetValue(key, out v) ? v : key;
        }

        private static readonly Dictionary<string, string> En = new Dictionary<string, string>
        {
            {"ImportTitle","Import Points"}, {"ExportTitle","Export Points"}, {"ConvertTitle","Coordinate Conversion"},
            {"SettingsTitle","Settings"}, {"AboutTitle","About & Help"},
            {"Help","Help"}, {"HelpTip","Explain this window (F1)"},
            {"File","File:"}, {"Browse","Browse..."}, {"ColumnOrder","Column order:"}, {"AutoDetect","Auto-detect"},
            {"ColumnOrderHint","P=Point  N=Northing  E=Easting  Z=Elevation  D=Description"},
            {"FileCrs","File coordinates:"}, {"ConvertTo","Convert to:"},
            {"Check","Data check"}, {"ExcludeDup","Exclude duplicates"}, {"ExcludeOut","Exclude outliers"}, {"IncludeAll","Include all"},
            {"DrawOptions","Drawing options"}, {"TextHeight","Text height:"}, {"Place3D","Place points at their elevation (3D)"},
            {"ShowName","Show point name"}, {"ShowElev","Show elevation"}, {"ShowDesc","Show description"}, {"ZoomAfter","Zoom to points after drawing"},
            {"DrawPoints","Draw points"}, {"Cancel","Cancel"}, {"Close","Close"}, {"Save","Save"}, {"SaveFile","Save file..."},
            {"ColName","Point"}, {"ColE","Easting / Long"}, {"ColN","Northing / Lat"}, {"ColZ","Elevation"}, {"ColDesc","Description"}, {"ColIssue","Check result"},
            {"IssueDupName","Duplicate name"}, {"IssueDupXY","Duplicate coordinates"}, {"IssueOutZ","Abnormal elevation"}, {"IssueOutXY","Far from other points"}, {"IssueNoZ","No elevation"},
            {"NoFileYet","Choose a file to see a preview and the data check."},
            {"SumTotal","Points: {0}   (selected: {1})"}, {"SumFormat","Columns:"},
            {"SumDup","Duplicates — names: {0}, coordinates: {1}"}, {"SumOut","Outliers — elevation: {0}, position: {1}"},
            {"SumZ","Elevation: {0} → {1}"}, {"SkippedLines","{0} line(s) could not be read and were skipped."},
            {"ReadError","Could not read the file."}, {"NothingToDraw","No points are selected for drawing."},
            {"GeoNeedsConvert","The file is in Lat/Long. Choose a projected system (e.g. UTM or DLTM) in \"Convert to\" before drawing."},
            {"From","From:"}, {"To","To:"}, {"Swap","⇅ Swap"}, {"TabSingle","Single point"}, {"TabFile","Whole file"}, {"TabDrawing","Points in drawing"},
            {"Easting","Easting (E):"}, {"Northing","Northing (N):"}, {"Longitude","Longitude:"}, {"Latitude","Latitude:"},
            {"Convert","Convert"}, {"PickInDrawing","Pick from drawing"}, {"Copy","Copy result"},
            {"BadNumber","Please enter valid numbers. Lat/Long may be decimal (25.2048) or DMS (25°12'17.3\")."},
            {"PickNeedsProjected","The picked point is in drawing units. Set \"From\" to the drawing's projected system."},
            {"InputFile","Input file:"}, {"OutputFile","Output file:"}, {"ConvertFile","Convert file"},
            {"FileConvertHint","Any supported point file is read (CSV, TXT, Excel, LandXML, KML, GeoJSON). Result is saved as CSV: Point, Northing, Easting, Elevation, Description. Elevations are kept as they are."},
            {"ChooseFiles","Choose the input and output files first."}, {"FileConverted","Done: {0} points converted."},
            {"DrawingConvertHint","Moves the selected points in the drawing (SurveyX points, blocks and AutoCAD POINTs) from the \"From\" system to the \"To\" system. Elevations are not changed. You can use UNDO to go back."},
            {"SelectAndTransform","Select points in drawing and transform"}, {"SameCrs","\"From\" and \"To\" are the same."},
            {"DrawingNeedsProjected","Both systems must be projected (not Lat/Long) to move drawing points."},
            {"PointsSelected","{0} points selected"}, {"Format","Format:"}, {"Decimals","Decimals:"}, {"WriteHeader","Write header row"},
            {"DrawingCrs","Drawing coordinates:"}, {"DrawingCrsHint","Needed only for KML / GeoJSON (Google Earth) to convert to Lat/Long."},
            {"Exported","{0} points exported."},
            {"Language","Language:"}, {"LayerPrefix","Layer prefix:"}, {"RowsPerTable","Rows per coordinate table:"},
            {"CheckUpdates","Check for updates when AutoCAD starts"}, {"BadPrefix","The layer prefix contains characters not allowed in layer names."},
            {"By","By"}, {"FreeForever","Free forever — no subscription, no licence."},
            {"CheckNow","Check for updates"}, {"DownloadPage","Download page"},
            {"NoReleaseYet","No published release was found yet."}, {"UpToDate","You have the latest version."},
            {"UpdateAvailable","Version {0} is available. Open the download page?"}, {"UpdateCheckFailed","Could not check for updates (check the internet connection)."},
            {"RibPoints","Points"}, {"RibCoords","Coordinates"}, {"RibTools","Tools"},
            {"RibImport","Import"}, {"RibExport","Export"}, {"RibAddPoint","Add Point"}, {"RibConvert","Convert"}, {"RibTable","Table"}, {"RibLabel","E/N Label"},
            {"RibSettings","Settings"}, {"RibAbout","Help"},
            {"TipImport","Import points from CSV, TXT, Excel, LandXML, KML or GeoJSON with preview and checks (SXIMPORT)"},
            {"TipExport","Export points to CSV, TXT, LandXML, KML or GeoJSON (SXEXPORT)"},
            {"TipAddPoint","Click to add numbered survey points (SXPOINT)"},
            {"TipConvert","Convert coordinates: UTM, DLTM, Nahrwan, Egyptian belts, WGS 84 (SXCONVERT)"},
            {"TipTable","Create a coordinates table from selected points (SXTABLE)"},
            {"TipLabel","Leader with Easting / Northing / Elevation of a picked point (SXLABEL)"},
            {"TipSettings","Language, text height, decimals, layers (SXSETTINGS)"}, {"TipAbout","Commands list, version and updates (SXHELP)"},
            {"HelpImport",
                "1) Press Browse and choose the points file.\n" +
                "2) The column order is detected automatically. If the preview looks wrong, choose the correct order (e.g. PENZD).\n" +
                "3) Choose the coordinate system of the file. To convert while importing, tick \"Convert to\" and choose the target system.\n" +
                "4) Rows in yellow are duplicates, rows in red are abnormal (far away or strange elevation). Untick them or use the buttons on the right.\n" +
                "5) Press \"Draw points\".\n\nLayers: SX_POINTS, SX_PNUM, SX_ELEV, SX_DESC."},
            {"HelpConvert",
                "Choose the system you have (From) and the one you need (To).\n\n" +
                "• Single point: type the coordinates or pick a point from the drawing.\n" +
                "• Whole file: converts every point in a file and saves a new CSV.\n" +
                "• Points in drawing: moves the drawn points to the new system.\n\n" +
                "Elevations are never changed. Datum shifts use the official EPSG parameters."},
            {"HelpExport","Select the format and press Save. For Google Earth (KML) choose the coordinate system of your drawing so the points land in the right place."},
            {"HelpSettings","Language changes take effect for new windows immediately. Text height is in drawing units (metres)."},
            {"HelpAbout","Type any command on the AutoCAD command line, or use the SurveyX ribbon tab."},
            {"CommandList",
                "Commands:\n" +
                "SXIMPORT  — Import points (CSV, TXT, Excel, LandXML, KML, GeoJSON)\n" +
                "SXEXPORT  — Export points\n" +
                "SXCONVERT — Coordinate conversion (point / file / drawing)\n" +
                "SXTABLE   — Coordinates table\n" +
                "SXLABEL   — E/N/Z leader label\n" +
                "SXPOINT   — Add points by clicking\n" +
                "SXSETTINGS — Settings\n" +
                "SXUPDATE  — Open the download page\n" +
                "SXRIBBON  — Show the SurveyX ribbon tab again\n\n" +
                "Supported systems: WGS 84, UTM 35–40N, DLTM (Dubai), Nahrwan 1967 (UAE / Abu Dhabi), Egypt 1907 Red / Purple / Extended Purple / Blue belts."},
        };

        private static readonly Dictionary<string, string> Ar = new Dictionary<string, string>
        {
            {"ImportTitle","استيراد النقط"}, {"ExportTitle","تصدير النقط"}, {"ConvertTitle","تحويل الإحداثيات"},
            {"SettingsTitle","الإعدادات"}, {"AboutTitle","عن الأداة والمساعدة"},
            {"Help","شرح"}, {"HelpTip","شرح الشاشة دي (F1)"},
            {"File","الملف:"}, {"Browse","اختيار..."}, {"ColumnOrder","ترتيب الأعمدة:"}, {"AutoDetect","تحديد تلقائي"},
            {"ColumnOrderHint","P=رقم النقطة  N=شمالي  E=شرقي  Z=منسوب  D=وصف"},
            {"FileCrs","نظام إحداثيات الملف:"}, {"ConvertTo","حوّل إلى:"},
            {"Check","فحص البيانات"}, {"ExcludeDup","استبعاد المكرر"}, {"ExcludeOut","استبعاد الشاذ"}, {"IncludeAll","اختيار الكل"},
            {"DrawOptions","خيارات الرسم"}, {"TextHeight","ارتفاع الكتابة:"}, {"Place3D","رسم النقط على منسوبها (3D)"},
            {"ShowName","إظهار رقم النقطة"}, {"ShowElev","إظهار المنسوب"}, {"ShowDesc","إظهار الوصف"}, {"ZoomAfter","زووم على النقط بعد الرسم"},
            {"DrawPoints","ارسم النقط"}, {"Cancel","إلغاء"}, {"Close","إغلاق"}, {"Save","حفظ"}, {"SaveFile","حفظ الملف..."},
            {"ColName","النقطة"}, {"ColE","شرقي / طول"}, {"ColN","شمالي / عرض"}, {"ColZ","المنسوب"}, {"ColDesc","الوصف"}, {"ColIssue","نتيجة الفحص"},
            {"IssueDupName","رقم مكرر"}, {"IssueDupXY","إحداثيات مكررة"}, {"IssueOutZ","منسوب غير طبيعي"}, {"IssueOutXY","بعيدة عن باقي النقط"}, {"IssueNoZ","مفيش منسوب"},
            {"NoFileYet","اختار ملف عشان تشوف المعاينة وفحص البيانات."},
            {"SumTotal","عدد النقط: {0}   (المختار: {1})"}, {"SumFormat","الأعمدة:"},
            {"SumDup","المكرر — أرقام: {0}، إحداثيات: {1}"}, {"SumOut","الشاذ — منسوب: {0}، مكان: {1}"},
            {"SumZ","المنسوب: من {0} إلى {1}"}, {"SkippedLines","{0} سطر مقدرتش أقراهم واتجاهلوا."},
            {"ReadError","مقدرتش أقرا الملف."}, {"NothingToDraw","مفيش نقط مختارة للرسم."},
            {"GeoNeedsConvert","الملف بخطوط الطول والعرض. اختار نظام مسقط (زي UTM أو DLTM) في \"حوّل إلى\" قبل الرسم."},
            {"From","من:"}, {"To","إلى:"}, {"Swap","⇅ تبديل"}, {"TabSingle","نقطة واحدة"}, {"TabFile","ملف كامل"}, {"TabDrawing","نقط في الرسمة"},
            {"Easting","الشرقي (E):"}, {"Northing","الشمالي (N):"}, {"Longitude","خط الطول:"}, {"Latitude","خط العرض:"},
            {"Convert","تحويل"}, {"PickInDrawing","اختيار من الرسمة"}, {"Copy","نسخ النتيجة"},
            {"BadNumber","اكتب أرقام صحيحة. خطوط الطول والعرض ممكن تكون عشري (25.2048) أو درجات ودقائق وثواني (25°12'17.3\")."},
            {"PickNeedsProjected","النقطة اللي اخترتها بوحدات الرسمة. خلّي \"من\" هو نظام الرسمة المسقط."},
            {"InputFile","ملف الإدخال:"}, {"OutputFile","ملف الناتج:"}, {"ConvertFile","حوّل الملف"},
            {"FileConvertHint","بيقرا أي ملف نقط (CSV, TXT, Excel, LandXML, KML, GeoJSON) ويحفظ الناتج CSV بالترتيب: رقم، شمالي، شرقي، منسوب، وصف. المناسيب بتفضل زي ما هي."},
            {"ChooseFiles","اختار ملف الإدخال والناتج الأول."}, {"FileConverted","تم: اتحولت {0} نقطة."},
            {"DrawingConvertHint","بينقل النقط المختارة في الرسمة (نقط SurveyX والبلوكات و POINT) من نظام \"من\" لنظام \"إلى\". المناسيب مش بتتغير. وتقدر ترجع بـ UNDO."},
            {"SelectAndTransform","اختار النقط من الرسمة وحوّلها"}, {"SameCrs","نظام \"من\" و \"إلى\" نفس النظام."},
            {"DrawingNeedsProjected","لازم النظامين يكونوا مسقطين (مش خطوط طول وعرض) عشان أنقل نقط الرسمة."},
            {"PointsSelected","تم اختيار {0} نقطة"}, {"Format","الصيغة:"}, {"Decimals","عدد الأرقام العشرية:"}, {"WriteHeader","كتابة سطر العناوين"},
            {"DrawingCrs","نظام إحداثيات الرسمة:"}, {"DrawingCrsHint","مطلوب بس في KML و GeoJSON (جوجل إيرث) عشان أحوّل لخطوط طول وعرض."},
            {"Exported","اتصدّرت {0} نقطة."},
            {"Language","اللغة:"}, {"LayerPrefix","بادئة أسماء الطبقات:"}, {"RowsPerTable","عدد الصفوف في جدول الإحداثيات:"},
            {"CheckUpdates","دوّر على تحديثات لما الأوتوكاد يفتح"}, {"BadPrefix","بادئة الطبقات فيها حروف مش مسموحة في أسماء الطبقات."},
            {"By","تطوير"}, {"FreeForever","مجانية للأبد — من غير اشتراك ولا ترخيص."},
            {"CheckNow","دوّر على تحديث"}, {"DownloadPage","صفحة التحميل"},
            {"NoReleaseYet","لسه مفيش إصدار منشور."}, {"UpToDate","عندك آخر إصدار."},
            {"UpdateAvailable","الإصدار {0} نزل. تفتح صفحة التحميل؟"}, {"UpdateCheckFailed","مقدرتش أدوّر على تحديث (اتأكد من الإنترنت)."},
            {"RibPoints","النقط"}, {"RibCoords","الإحداثيات"}, {"RibTools","أدوات"},
            {"RibImport","استيراد"}, {"RibExport","تصدير"}, {"RibAddPoint","إضافة نقطة"}, {"RibConvert","تحويل"}, {"RibTable","جدول"}, {"RibLabel","ليدر إحداثيات"},
            {"RibSettings","الإعدادات"}, {"RibAbout","مساعدة"},
            {"TipImport","استيراد نقط من CSV و TXT و Excel و LandXML و KML و GeoJSON مع معاينة وفحص (SXIMPORT)"},
            {"TipExport","تصدير النقط لـ CSV و TXT و LandXML و KML و GeoJSON (SXEXPORT)"},
            {"TipAddPoint","اضغط في الرسمة لإضافة نقط مترقمة (SXPOINT)"},
            {"TipConvert","تحويل إحداثيات: UTM و DLTM ونهروان والأحزمة المصرية و WGS 84 (SXCONVERT)"},
            {"TipTable","عمل جدول إحداثيات للنقط المختارة (SXTABLE)"},
            {"TipLabel","ليدر عليه الشرقي والشمالي والمنسوب لأي نقطة (SXLABEL)"},
            {"TipSettings","اللغة وارتفاع الكتابة والأرقام العشرية والطبقات (SXSETTINGS)"}, {"TipAbout","قائمة الأوامر والإصدار والتحديثات (SXHELP)"},
            {"HelpImport",
                "1) دوس \"اختيار\" واختار ملف النقط.\n" +
                "2) ترتيب الأعمدة بيتحدد لوحده. لو المعاينة شكلها غلط، اختار الترتيب الصح (مثلاً PENZD).\n" +
                "3) اختار نظام إحداثيات الملف. لو عايز تحوّل وانت بتستورد، علّم على \"حوّل إلى\" واختار النظام المطلوب.\n" +
                "4) الصفوف الصفرا مكررة، والحمرا شاذة (بعيدة أو منسوبها غريب). شيل العلامة منها أو استخدم الأزرار اللي على الجنب.\n" +
                "5) دوس \"ارسم النقط\".\n\nالطبقات: SX_POINTS و SX_PNUM و SX_ELEV و SX_DESC."},
            {"HelpConvert",
                "اختار النظام اللي معاك (من) والنظام اللي محتاجه (إلى).\n\n" +
                "• نقطة واحدة: اكتب الإحداثيات أو اختار نقطة من الرسمة.\n" +
                "• ملف كامل: بيحوّل كل النقط اللي في الملف ويحفظ CSV جديد.\n" +
                "• نقط في الرسمة: بينقل النقط المرسومة للنظام الجديد.\n\n" +
                "المناسيب عمرها ما بتتغير. تحويل الداتم بيستخدم بارامترات EPSG الرسمية."},
            {"HelpExport","اختار الصيغة ودوس حفظ. لو هتفتح في جوجل إيرث (KML) اختار نظام إحداثيات الرسمة عشان النقط تقع في مكانها الصح."},
            {"HelpSettings","تغيير اللغة بيظهر على طول في الشاشات الجديدة. ارتفاع الكتابة بوحدات الرسمة (متر)."},
            {"HelpAbout","اكتب أي أمر في سطر أوامر الأوتوكاد، أو استخدم تبويب SurveyX في الشريط."},
            {"CommandList",
                "الأوامر:\n" +
                "SXIMPORT  — استيراد النقط (CSV, TXT, Excel, LandXML, KML, GeoJSON)\n" +
                "SXEXPORT  — تصدير النقط\n" +
                "SXCONVERT — تحويل الإحداثيات (نقطة / ملف / رسمة)\n" +
                "SXTABLE   — جدول إحداثيات\n" +
                "SXLABEL   — ليدر بالشرقي والشمالي والمنسوب\n" +
                "SXPOINT   — إضافة نقط بالضغط\n" +
                "SXSETTINGS — الإعدادات\n" +
                "SXUPDATE  — فتح صفحة التحميل\n" +
                "SXRIBBON  — إظهار تبويب SurveyX تاني\n\n" +
                "الأنظمة المدعومة: WGS 84، و UTM من 35 لـ 40 شمال، و DLTM دبي، ونهروان 1967 (الإمارات / أبوظبي)، والأحزمة المصرية: الأحمر والبنفسجي والبنفسجي الممتد والأزرق."},
        };
    }
}
