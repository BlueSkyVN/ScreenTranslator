; ============================================
; Inno Setup Script cho Screen Translator Pro
; ============================================
; Yêu cầu: Tải Inno Setup 6.x từ https://jrsoftware.org/isdl.php
; Để build installer: mở file này trong Inno Setup Compiler → Build → Compile
;
; TRƯỚC KHI BUILD INSTALLER:
; 1. Build dự án Release: dotnet publish -c Release -r win-x64 --self-contained
; 2. Đặt đường dẫn output ở biến {#PublishDir} bên dưới
; ============================================

#define AppName "Screen Translator Pro"
#define AppVersion "3.0"
#define AppPublisher "BlueSkyVN"
#define AppURL "https://github.com/BlueSkyVN/ScreenTranslator"
#define AppExeName "ScreenTranslator.exe"

; === CHỈNH ĐƯỜNG DẪN NÀY CHO ĐÚNG VỚI MÁY BẠN ===
#define PublishDir "..\bin\Release\net10.0-windows10.0.19041.0\win-x64\publish"

[Setup]
AppId={{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppURL}
AppSupportURL={#AppURL}
AppUpdatesURL={#AppURL}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
LicenseFile=
OutputDir=Output
OutputBaseFilename=ScreenTranslatorPro_Setup_v{#AppVersion}
SetupIconFile=..\app.ico
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\{#AppExeName}

[Languages]
Name: "vietnamese"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Tạo biểu tượng trên Desktop"; GroupDescription: "Biểu tượng bổ sung:"
Name: "startupicon"; Description: "Khởi động cùng Windows"; GroupDescription: "Tùy chọn khởi động:"; Flags: unchecked

[Files]
; Sao chép toàn bộ thư mục publish vào thư mục cài đặt
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

; Sao chép thư mục mô hình offline (nếu có)
Source: "..\local_model\*"; DestDir: "{app}\local_model"; Flags: ignoreversion recursesubdirs createallsubdirs skipifsourcedoesntexist; Check: DirExists(ExpandConstant('{src}\local_model'))

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{group}\Gỡ cài đặt {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Registry]
; Tự động khởi động cùng Windows (nếu người dùng chọn)
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "{#AppName}"; ValueData: """{app}\{#AppExeName}"""; Flags: uninsdeletevalue; Tasks: startupicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Khởi chạy {#AppName}"; Flags: nowait postinstall skipifsilent


