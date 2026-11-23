; SurveyX CAD installer — per-user, no admin rights needed.
; Installs an AutoCAD Autoloader bundle that AutoCAD / Civil 3D 2021-2026 load automatically.
#ifndef AppVer
  #define AppVer "1.0.0"
#endif

[Setup]
AppId={{11914421-1C44-4D52-858F-7BB82781094F}
AppName=SurveyX CAD
AppVersion={#AppVer}
AppVerName=SurveyX CAD {#AppVer}
AppPublisher=Ahmed Gamal Shalaby
AppPublisherURL=https://github.com/SurveyX-Tools/surveyx-cad
DefaultDirName={userappdata}\Autodesk\ApplicationPlugins\SurveyXCAD.bundle
DisableDirPage=yes
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=..\out
OutputBaseFilename=SurveyXCAD_Setup_{#AppVer}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayName=SurveyX CAD
CloseApplications=no

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"

[Messages]
WelcomeLabel2=This will install [name/ver] — free surveying tools for AutoCAD and Civil 3D 2021-2026.%n%nPlease close AutoCAD before continuing.%n%nسيتم تثبيت أدوات SurveyX المجانية للأوتوكاد وسيفيل ثري دي. اقفل الأوتوكاد قبل المتابعة.
FinishedLabel=SurveyX CAD is installed. Open AutoCAD or Civil 3D and look for the "SurveyX" ribbon tab, or type SXHELP.%n%nتم التثبيت. افتح الأوتوكاد وهتلاقي تبويب SurveyX، أو اكتب SXHELP.

[Files]
Source: "..\out\bundle\SurveyXCAD.bundle\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[UninstallDelete]
Type: filesandordirs; Name: "{app}"
