#define MyAppName "Vaani BI"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "Your Name"
#define MyAppExeName "vconsole.exe"
#define MyAppIconName "icon.ico"
#define VBCableURL "https://download.vb-audio.com/Download_CABLE/VBCABLE_Driver_Pack43.zip"

[Setup]
AppId={{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
OutputDir=installer
OutputBaseFilename=VaaniSetup
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesInstallIn64BitMode=x64
; Minimize installation steps
DisableProgramGroupPage=yes
DisableReadyPage=yes
DisableWelcomePage=no
AlwaysShowComponentsList=false
; Set icon for setup.exe
SetupIconFile=Assets\icon.ico
; Set icon for Control Panel (Add/Remove Programs)
UninstallDisplayIcon={app}\{#MyAppIconName}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "installvbcable"; Description: "Download and install VB-CABLE Audio Driver (Required for translation)"; GroupDescription: "Required Components:"

[Files]
Source: "bin\Release\net8.0\win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs
; Explicitly copy icon file to ensure it's available for shortcuts
Source: "Assets\icon.ico"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\{#MyAppIconName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\{#MyAppIconName}"; Tasks: desktopicon

[Run]
Filename: "{tmp}\VBCABLE_Setup_x64.exe"; Parameters: "-i -h"; Description: "Install VB-CABLE Driver"; Flags: runhidden waituntilterminated skipifdoesntexist; StatusMsg: "Installing VB-CABLE audio driver..."; Check: ShouldInstallVBCable
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[Code]
var
  DownloadPage: TDownloadWizardPage;
  VBCableDownloaded: Boolean;

function BoolToInt(B: Boolean): Integer;
begin
  if B then
    Result := 1
  else
    Result := 0;
end;

function OnDownloadProgress(const Url, FileName: String; const Progress, ProgressMax: Int64): Boolean;
begin
  if Progress = ProgressMax then
    Log(Format('Successfully downloaded %s to %s', [FileName, ExpandConstant('{tmp}')]));
  Result := True;
end;

function IsVBCableInstalled: Boolean;
var
  DeviceKey: String;
  I: Integer;
  DeviceName: String;
begin
  Result := False;
  
  // Method 1: Check for device driver in registry
  DeviceKey := 'SYSTEM\CurrentControlSet\Control\Class\{4d36e96c-e325-11ce-bfc1-08002be10318}';
  
  if RegKeyExists(HKEY_LOCAL_MACHINE, DeviceKey) then begin
    // Enumerate subkeys to find VB-CABLE device
    for I := 0 to 999 do begin
      if RegQueryStringValue(HKEY_LOCAL_MACHINE, DeviceKey + '\' + Format('%.4d', [I]), 'DriverDesc', DeviceName) then begin
        if Pos('VB-Audio Virtual Cable', DeviceName) > 0 then begin
          Result := True;
          Log('VB-CABLE device found: ' + DeviceName);
          Exit;
        end;
      end;
    end;
  end;
  
  // Method 2: Check MME devices registry
  if RegKeyExists(HKEY_LOCAL_MACHINE, 'SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\Render') or
     RegKeyExists(HKEY_LOCAL_MACHINE, 'SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\Capture') then begin
    Log('Checking MME audio devices for VB-CABLE...');
    // Additional check could be added here
  end;
  
  if Result then
    Log('VB-CABLE is already installed')
  else
    Log('VB-CABLE is not installed');
end;

function IsVBCableNeeded: Boolean;
begin
  Result := not IsVBCableInstalled;
  if Result then
    Log('VB-CABLE installation is needed')
  else
    Log('VB-CABLE installation skipped - already installed');
end;

function ShouldInstallVBCable: Boolean;
var
  TaskSelected: Boolean;
  CableNeeded: Boolean;
begin
  TaskSelected := IsTaskSelected('installvbcable');
  CableNeeded := IsVBCableNeeded;
  Result := TaskSelected and CableNeeded and VBCableDownloaded;
  
  // Simplified logging to avoid multi-line array issues
  if Result then
    Log('ShouldInstallVBCable: YES - All conditions met')
  else
    Log('ShouldInstallVBCable: NO - Some conditions not met');
end;

procedure InitializeWizard;
begin
  VBCableDownloaded := False;
  
  WizardForm.WelcomeLabel2.Caption := 
    'This will install Vaani BI on your computer.' + #13#10#13#10 +
    'IMPORTANT: This application requires VB-CABLE Audio Driver to function properly. ' +
    'The installer will download and install it automatically if needed.' + #13#10#13#10 +
    'Click Next to continue.';
    
  DownloadPage := CreateDownloadPage(SetupMessage(msgWizardPreparing), SetupMessage(msgPreparingDesc), @OnDownloadProgress);
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  ErrorCode: Integer;
  ZipFile: String;
  ExtractPath: String;
begin
  Result := True;
  
  if CurPageID = wpReady then begin
    if IsTaskSelected('installvbcable') and IsVBCableNeeded then begin
      DownloadPage.Clear;
      DownloadPage.Add('{#VBCableURL}', 'VBCABLE_Driver_Pack43.zip', '');
      DownloadPage.Show;
      
      try
        try
          DownloadPage.Download;
          Log('VB-CABLE download completed');
          
          // Extract the ZIP file using PowerShell
          ZipFile := ExpandConstant('{tmp}\VBCABLE_Driver_Pack43.zip');
          ExtractPath := ExpandConstant('{tmp}');
          
          Log(Format('Extracting %s to %s', [ZipFile, ExtractPath]));
          
          if Exec(ExpandConstant('{cmd}'), 
                 '/c powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "Expand-Archive -Path ''' + ZipFile + ''' -DestinationPath ''' + ExtractPath + ''' -Force"',
                 '', SW_HIDE, ewWaitUntilTerminated, ErrorCode) then
          begin
            if ErrorCode = 0 then begin
              Log('VB-CABLE extracted successfully');
              // Verify the extracted file exists
              if FileExists(ExpandConstant('{tmp}\VBCABLE_Setup_x64.exe')) then begin
                VBCableDownloaded := True;
                Log('VBCABLE_Setup_x64.exe verified at: ' + ExpandConstant('{tmp}\VBCABLE_Setup_x64.exe'));
              end else begin
                Log('ERROR: VBCABLE_Setup_x64.exe not found after extraction');
                MsgBox('VB-CABLE installer not found after extraction.', mbError, MB_OK);
                Result := False;
              end;
            end else begin
              Log(Format('PowerShell extraction failed with code %d', [ErrorCode]));
              MsgBox('Failed to extract VB-CABLE installer. Error code: ' + IntToStr(ErrorCode), mbError, MB_OK);
              Result := False;
            end;
          end else begin
            Log('Failed to execute PowerShell extraction command');
            MsgBox('Failed to extract VB-CABLE installer. PowerShell may not be available.', mbError, MB_OK);
            Result := False;
          end;
          
        except
          Log('Exception occurred during VB-CABLE download');
          if MsgBox('Failed to download VB-CABLE Audio Driver.' + #13#10#13#10 +
                    'The application requires this driver to function.' + #13#10#13#10 +
                    'Do you want to continue without installing VB-CABLE?' + #13#10 +
                    '(You can download it manually from vb-audio.com)', 
                    mbError, MB_YESNO) = IDYES then
          begin
            Result := True;
            VBCableDownloaded := False;
          end else
            Result := False;
        end;
      finally
        DownloadPage.Hide;
      end;
    end;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ErrorCode: Integer;
  NeedsReboot: Boolean;
begin
  if CurStep = ssPostInstall then begin
    NeedsReboot := False;
    
    if IsTaskSelected('installvbcable') then begin
      if VBCableDownloaded then begin
        // Check if installation was successful
        Sleep(2000); // Wait for driver installation to complete
        if IsVBCableInstalled then begin
          NeedsReboot := True;
          Log('VB-CABLE successfully installed');
        end else begin
          Log('VB-CABLE installation may have failed - device not detected yet');
          NeedsReboot := True; // Still might need reboot
        end;
      end;
      
      // Show appropriate message
      if NeedsReboot then begin
        MsgBox('VB-CABLE driver has been installed.' + #13#10#13#10 +
                'IMPORTANT: You must restart your computer for the audio driver to work properly.' + #13#10#13#10 +
                'After restart, configure your meeting apps:' + #13#10 +
                '  • Meeting Microphone: CABLE Output' + #13#10 +
                '  • Meeting Speaker: CABLE Input', 
                mbInformation, MB_OK);
      end else if not IsVBCableInstalled and IsTaskSelected('installvbcable') then begin
        if MsgBox('VB-CABLE driver installation could not be completed.' + #13#10#13#10 +
                  'Would you like to download it manually from the official website?',
                  mbConfirmation, MB_YESNO) = IDYES then
        begin
          ShellExec('open', 'https://vb-audio.com/Cable/', '', '', SW_SHOW, ewNoWait, ErrorCode);
        end;
      end;
    end;
  end;
end;