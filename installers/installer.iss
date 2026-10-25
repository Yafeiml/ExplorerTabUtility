; The version will be set by the build script
#ifndef MyAppVersion
  #define MyAppVersion "v1.0.0"
#endif

; Extract numeric version for VersionInfoVersion (remove 'v' and any suffix after a dash)
#define MyAppVersionWithoutV StringChange(MyAppVersion, "v", "")
#define DashPos Pos("-", MyAppVersionWithoutV)
#define MyAppNumericVersion (DashPos > 0) ? Copy(MyAppVersionWithoutV, 1, DashPos - 1) : MyAppVersionWithoutV

#define MyAppPublisher "Yafeiml"
#define MyAppName "ExplorerTabUtility"
#define MyAppExeName MyAppName + ".exe"
#define MyAppRelativePath MyAppName + "\" + MyAppExeName
#define MyAppURL "https://github.com/Yafeiml/ExplorerTabUtility"
#define DotNet10InstallerUrl "https://aka.ms/dotnet/10.0/windowsdesktop-runtime-win-x64.exe"
#define DotNet10InstallerUrlX86 "https://aka.ms/dotnet/10.0/windowsdesktop-runtime-win-x86.exe"
#define DotNet10InstallerUrlArm64 "https://aka.ms/dotnet/10.0/windowsdesktop-runtime-win-arm64.exe"
#define DotNet10Version "10.0"

; Source directory for build artifacts
#ifndef SourceDir
  #define SourceDir "..\artifacts"
#endif

[Setup]
AppId={{E1F2A3C4-F5D4-3B2E-1D5A-8F7B2F8D3E7A}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={userpf}\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
PrivilegesRequired=lowest
OutputDir={#SourceDir}
OutputBaseFilename={#MyAppName}_{#MyAppVersion}_Setup
SetupIconFile=..\{#MyAppName}\Icon.ico
Compression=lzma
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64 arm64
UninstallDisplayIcon={app}\{#MyAppRelativePath}
UninstallDisplayName={#MyAppName}
CloseApplications=yes
RestartApplications=no

; Add version information to the installer
VersionInfoVersion={#MyAppNumericVersion}
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription={#MyAppName} Installer
VersionInfoTextVersion={#MyAppVersion}
VersionInfoCopyright= {#MyAppPublisher}
VersionInfoProductName={#MyAppName}
VersionInfoProductVersion={#MyAppNumericVersion}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "startupicon"; Description: "Start with Windows"; GroupDescription: "Windows Integration"; Flags: unchecked

[Files]
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion

; .NET 10.0 Framework-dependent packages
Source: "{#SourceDir}\{#MyAppName}_{#MyAppVersion}_x86_Net10.0_FrameworkDependent.zip"; DestDir: "{app}"; Flags: ignoreversion; Check: IsX86AndDotNet10Available
Source: "{#SourceDir}\{#MyAppName}_{#MyAppVersion}_x64_Net10.0_FrameworkDependent.zip"; DestDir: "{app}"; Flags: ignoreversion; Check: IsX64AndDotNet10Available
Source: "{#SourceDir}\{#MyAppName}_{#MyAppVersion}_arm64_Net10.0_FrameworkDependent.zip"; DestDir: "{app}"; Flags: ignoreversion; Check: IsArm64AndDotNet10Available

; .NET Framework 4.8.1 packages
Source: "{#SourceDir}\{#MyAppName}_{#MyAppVersion}_x86_NetFW4.8.1.zip"; DestDir: "{app}"; Flags: ignoreversion; Check: IsX86AndDotNet10NotAvailable
Source: "{#SourceDir}\{#MyAppName}_{#MyAppVersion}_x64_NetFW4.8.1.zip"; DestDir: "{app}"; Flags: ignoreversion; Check: IsX64AndDotNet10NotAvailable
Source: "{#SourceDir}\{#MyAppName}_{#MyAppVersion}_arm64_NetFW4.8.1.zip"; DestDir: "{app}"; Flags: ignoreversion; Check: IsArm64AndDotNet10NotAvailable

[Dirs]
; Create app directory with uninstall flag to ensure it's removed on uninstall
Name: "{app}"; Flags: uninsalwaysuninstall

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppRelativePath}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppRelativePath}"; Tasks: desktopicon

[Registry]
; Add to startup if the startup task is selected
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "{#MyAppName}"; ValueData: """{app}\{#MyAppRelativePath}"""; Flags: uninsdeletevalue; Tasks: startupicon
; Set StartupApproved registry entry (enabled = 02 00 00 00 00 00 00 00 00 00 00 00)
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run"; ValueType: binary; ValueName: "{#MyAppName}"; ValueData: 02 00 00 00 00 00 00 00 00 00 00 00; Flags: uninsdeletevalue; Tasks: startupicon

[InstallDelete]
; Clean the app directory before installation to avoid leftover files
Type: filesandordirs; Name: "{app}\*"

[UninstallDelete]
; Ensure all files in the app directory are removed during uninstall
Type: filesandordirs; Name: "{app}\*"
Type: dirifempty; Name: "{app}"

[Run]
; Extract the ZIP file after installation
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -Command ""$ErrorActionPreference = 'Stop'; $zipFile = '{app}\*.zip'; Write-Host 'Extracting application files...'; Expand-Archive -Path $zipFile -DestinationPath '{app}' -Force; Remove-Item -Path $zipFile -Force; Write-Host 'Installation complete.'"""; StatusMsg: "Extracting files..."; Flags: runhidden

; Launch the application after installation if selected
Filename: "{app}\{#MyAppRelativePath}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[Code]
// Global variables
var
  DotNet10Detected: Boolean;
  UseDotNet10: Boolean;
  DownloadPage: TDownloadWizardPage;
  
// Custom uninstall procedure to remove startup registry entries
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
  begin
    // Remove startup registry entries regardless of how they were added
    RegDeleteValue(HKEY_CURRENT_USER, 'Software\Microsoft\Windows\CurrentVersion\Run', '{#MyAppName}');
    RegDeleteValue(HKEY_CURRENT_USER, 'Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run', '{#MyAppName}');
  end;
end;
  
//------------------------------------------------------------------------------
// Runtime and architecture detection functions
//------------------------------------------------------------------------------

// Check if .NET 10 Desktop Runtime is installed
function IsDotNet10Installed: Boolean;
var
  ResultCode: Integer;
begin
  // Check if dotnet command is available and if .NET 10 Desktop Runtime is installed
  if Exec('cmd.exe', '/c dotnet --list-runtimes | find "Microsoft.WindowsDesktop.App {#DotNet10Version}"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    Result := (ResultCode = 0)
  else
    Result := False;
end;

// Architecture detection functions
function IsX86: Boolean;
begin
  Result := not IsWin64;
end;

function IsX64: Boolean;
begin
  Result := IsWin64 and (ProcessorArchitecture = paX64);
end;

function IsArm64: Boolean;
begin
  Result := IsWin64 and (ProcessorArchitecture = paARM64);
end;

// Get architecture description string
function GetArchitectureString: String;
begin
  if IsX86 then
    Result := 'x86'
  else if IsArm64 then
    Result := 'ARM64'
  else
    Result := 'x64';
end;

// Function to determine if .NET 10 installation is needed
function NeedsDotNet10Installation: Boolean;
begin
  Result := not DotNet10Detected and UseDotNet10;
end;

//------------------------------------------------------------------------------
// .NET 10 installer helper functions
//------------------------------------------------------------------------------

// Get the appropriate .NET 10 installer URL based on architecture
function GetDotNet10Url(Param: string): string;
begin
  if IsX86 then
    Result := '{#DotNet10InstallerUrlX86}'
  else if IsArm64 then
    Result := '{#DotNet10InstallerUrlArm64}'
  else
    Result := '{#DotNet10InstallerUrl}';  // Default to x64
end;

// Get the appropriate .NET 10 installer filename based on architecture
function GetDotNet10Filename: string;
begin
  if IsX86 then
    Result := 'dotnet10-x86.exe'
  else if IsArm64 then
    Result := 'dotnet10-arm64.exe'
  else
    Result := 'dotnet10-x64.exe';  // Default to x64
end;

//------------------------------------------------------------------------------
// File selection check functions
//------------------------------------------------------------------------------

// .NET 10 package selection checks
function IsX86AndDotNet10Available: Boolean;
begin
  Result := IsX86 and UseDotNet10;
end;

function IsX64AndDotNet10Available: Boolean;
begin
  Result := IsX64 and UseDotNet10;
end;

function IsArm64AndDotNet10Available: Boolean;
begin
  Result := IsArm64 and UseDotNet10;
end;

// .NET Framework 4.8.1 package selection checks
function IsX86AndDotNet10NotAvailable: Boolean;
begin
  Result := IsX86 and not UseDotNet10;
end;

function IsX64AndDotNet10NotAvailable: Boolean;
begin
  Result := IsX64 and not UseDotNet10;
end;

function IsArm64AndDotNet10NotAvailable: Boolean;
begin
  Result := IsArm64 and not UseDotNet10;
end;

//------------------------------------------------------------------------------
// Download and installation functions
//------------------------------------------------------------------------------

// Download progress callback
function OnDownloadProgress(const Url, FileName: String; const Progress, ProgressMax: Int64): Boolean;
begin
  if Progress = ProgressMax then
    Log(Format('Successfully downloaded file to %s', [FileName]));
  Result := True;
end;

// Handle download errors
procedure HandleDownloadError(const ErrorMessage: String);
begin
  // Log the error for silent mode
  Log('Download error: ' + ErrorMessage + '. Using .NET Framework 4.8.1 instead.');
  
  if Pos('12007', ErrorMessage) > 0 then
    SuppressibleMsgBox('No internet connection available. The application will use .NET Framework 4.8.1 instead.', 
                       mbInformation, MB_OK, MB_OK)
  else if Pos('aborted', ErrorMessage) > 0 then
    Log('Download was aborted by user. Using .NET Framework 4.8.1 instead.')
  else if not DownloadPage.AbortedByUser then
    SuppressibleMsgBox('Failed to download .NET 10 Desktop Runtime: ' + ErrorMessage + #13#10 + 
                       'The application will use .NET Framework 4.8.1 instead.', 
                       mbInformation, MB_OK, MB_OK);
end;

// Function to download and install .NET 10 Runtime
function DownloadAndInstallDotNet10: Boolean;
var
  DotNetInstallerPath: String;
  ResultCode: Integer;
  ErrorMessage: String;
  ArchString: String;
begin
  Result := False;
  ArchString := GetArchitectureString;
  
  // Configure download page
  DownloadPage.Clear;
  DownloadPage.Add(GetDotNet10Url(''), GetDotNet10Filename(), '');
  
  try
    // Set download page text
    DownloadPage.SetText('Downloading .NET 10 Desktop Runtime (' + ArchString + ')',
                         'Please wait while the installer downloads the required files...');

    // Show the download page
    DownloadPage.Show;
    
    // Try to download the file
    try
      DownloadPage.Download;
      DotNetInstallerPath := ExpandConstant('{tmp}\') + GetDotNet10Filename();
      
      // Check if the file exists
      if FileExists(DotNetInstallerPath) then
        Result := True
      else
      begin
        Log('Downloaded file is missing');
        Result := False;
      end;
    except
      ErrorMessage := GetExceptionMessage;
      Log('Download exception: ' + ErrorMessage);
      HandleDownloadError(ErrorMessage);
      Result := False;
    end;
    
    // If download was successful, run the installer
    if Result then
    begin
      DownloadPage.SetText('Installing .NET 10 Desktop Runtime...', 'This may take a few minutes...');
      DownloadPage.SetProgress(0, 100);
      
      try
        // Run the installer with UI visible
        if Exec(DotNetInstallerPath, '/install /passive /norestart', '', SW_SHOW, ewWaitUntilTerminated, ResultCode) then
        begin
          // Check if installation was successful
          if ResultCode = 0 then
          begin
            Log('Successfully installed .NET 10 Desktop Runtime');
            DownloadPage.SetProgress(100, 100);
            Result := True;
            
            // Verify installation was successful by checking again
            if not IsDotNet10Installed then
            begin
              Log('Installation completed but .NET 10 Desktop Runtime is still not detected');
              SuppressibleMsgBox('Installation completed but .NET 10 Desktop Runtime is still not detected. ' +
                                'The application will use .NET Framework 4.8.1 instead.', 
                                mbInformation, MB_OK, MB_OK);
              Result := False;
            end;
          end
          else
          begin
            Log(Format('Failed to install .NET 10 Desktop Runtime. Exit code: %d', [ResultCode]));
            SuppressibleMsgBox('Failed to install .NET 10 Desktop Runtime. The application will use .NET Framework 4.8.1 instead.', 
                              mbInformation, MB_OK, MB_OK);
            Result := False;
          end;
        end
        else
        begin
          Log('Failed to execute .NET 10 Desktop Runtime installer');
          SuppressibleMsgBox('Failed to execute .NET 10 Desktop Runtime installer. The application will use .NET Framework 4.8.1 instead.', 
                            mbInformation, MB_OK, MB_OK);
          Result := False;
        end;
      except
        ErrorMessage := GetExceptionMessage;
        Log('Installation exception: ' + ErrorMessage);
        SuppressibleMsgBox('Error during .NET 10 installation: ' + ErrorMessage + #13#10 + 
                          'The application will use .NET Framework 4.8.1 instead.', 
                          mbInformation, MB_OK, MB_OK);
        Result := False;
      end;
    end;
  finally
    DownloadPage.Hide;
  end;
end;

//------------------------------------------------------------------------------
// Wizard event handlers
//------------------------------------------------------------------------------

// Before installation, check if we need to install .NET 10
function NextButtonClick(CurPageID: Integer): Boolean;
var
  ErrorMessage: String;
begin
  Result := True;
  
  if (CurPageID = wpReady) and NeedsDotNet10Installation then
  begin
    try
      Result := DownloadAndInstallDotNet10;
    except
      ErrorMessage := GetExceptionMessage;
      Log('Unexpected error in .NET 10 installation process: ' + ErrorMessage);
      Result := False;
    end;
    
    if not Result then
    begin
      // If .NET 10 installation failed, fall back to .NET Framework 4.8.1
      UseDotNet10 := False;
      Result := True;
      Log('Falling back to .NET Framework 4.8.1');
    end;
  end;
end;

// Before uninstallation, make sure the application is closed
function InitializeUninstall(): Boolean;
var
  ResultCode: Integer;
begin
  // Try to close the application if it's running
  Exec('taskkill.exe', '/f /im {#MyAppExeName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := True;
end;

// Initialize setup
procedure InitializeWizard;
begin
  // Check if .NET 10 Desktop Runtime is installed
  DotNet10Detected := IsDotNet10Installed;
  
  // Default to using .NET 10 (will be installed if not detected)
  // In silent mode, default to .NET Framework 4.8.1 if .NET 10 is not installed
  if WizardSilent and not DotNet10Detected then
  begin
    UseDotNet10 := False;
    Log('Silent mode: .NET 10 not detected, defaulting to .NET Framework 4.8.1');
  end
  else
  begin
    UseDotNet10 := True;
  end;
  
  // Create the download page for .NET 10 installer
  DownloadPage := CreateDownloadPage(SetupMessage(msgWizardPreparing), 
    SetupMessage(msgPreparingDesc), @OnDownloadProgress);
  
  // If .NET 10 is not detected and not in silent mode, inform the user that it will be installed
  if not DotNet10Detected and not WizardSilent then
  begin
    if MsgBox('.NET 10 Desktop Runtime is required but not installed.' + #13#10 + 
             'Would you like to download and install it now?' + #13#10 + #13#10 +
             'Yes: Install .NET 10 (Recommended)' + #13#10 +
             'No: Use .NET Framework 4.8.1 instead',
             mbConfirmation, MB_YESNO) = IDYES then
    begin
      UseDotNet10 := True;
    end
    else
    begin
      UseDotNet10 := False;
    end;
  end;
end;
