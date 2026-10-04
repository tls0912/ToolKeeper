; -*- coding: utf-8 -*-
; Offline, current-user Windows installer. Build arguments are supplied by
; scripts/Publish-TransLampInstaller.ps1; no resources are downloaded at install time.
Unicode true

!ifndef APP_VERSION
  !error "APP_VERSION is required."
!endif
!ifndef PAYLOAD_DIR
  !error "PAYLOAD_DIR is required."
!endif
!ifndef OUTPUT_FILE
  !error "OUTPUT_FILE is required."
!endif
!ifndef UNINSTALL_FILES
  !error "UNINSTALL_FILES is required."
!endif
!ifndef INSTALLED_SIZE_KB
  !error "INSTALLED_SIZE_KB is required."
!endif

!include "MUI2.nsh"
!include "LogicLib.nsh"
!include "FileFunc.nsh"
!include "WordFunc.nsh"
!include "WinVer.nsh"

!define UNINSTALL_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\ToolKeeper.TransLamp"
!define INSTALL_MARKER ".translamp-install.ini"
!define PRODUCT_ID "toolkeeper.translamp.installer"
!define VC_RUNTIME_KEY "SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64"
!define VC_MINIMUM_VERSION "14.44.35211.0"
!define APP_ICON "${__FILEDIR__}\..\..\src\TransLamp\Resources\TransLamp.ico"

Name "TransLamp ${APP_VERSION}"
OutFile "${OUTPUT_FILE}"
InstallDir "$LOCALAPPDATA\Programs\ToolKeeper\TransLamp"
RequestExecutionLevel user
SetCompressor zlib
CRCCheck on
ShowInstDetails show
ShowUninstDetails show
VIProductVersion "${APP_VERSION}.0"
VIAddVersionKey /LANG=1033 "ProductName" "TransLamp"
VIAddVersionKey /LANG=1033 "FileDescription" "TransLamp Offline Installer"
VIAddVersionKey /LANG=1033 "FileVersion" "${APP_VERSION}"
VIAddVersionKey /LANG=1033 "ProductVersion" "${APP_VERSION}"
VIAddVersionKey /LANG=1033 "LegalCopyright" "ToolKeeper"

Var RegisteredInstallDir
Var VcRuntimeReady
Var UninstallDeleteFailed

!define MUI_ICON "${APP_ICON}"
!define MUI_UNICON "${APP_ICON}"
!define MUI_ABORTWARNING
!define MUI_LANGDLL_REGISTRY_ROOT "HKCU"
!define MUI_LANGDLL_REGISTRY_KEY "${UNINSTALL_KEY}"
!define MUI_LANGDLL_REGISTRY_VALUENAME "InstallerLanguage"
!define MUI_LANGDLL_WINDOWTITLE "TransLamp"
!define MUI_LANGDLL_INFO "請選擇安裝語言 / Please select the installer language."
!define MUI_WELCOMEPAGE_TEXT "$(WelcomeText)"
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_COMPONENTS
!define MUI_PAGE_CUSTOMFUNCTION_LEAVE ValidateInstallDirectory
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!define MUI_FINISHPAGE_RUN "$INSTDIR\TransLamp.exe"
!define MUI_FINISHPAGE_RUN_NOTCHECKED
!define MUI_FINISHPAGE_NOREBOOTSUPPORT
!insertmacro MUI_PAGE_FINISH
!define MUI_UNCONFIRMPAGE_TEXT_TOP "$(UninstallText)"
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_UNPAGE_FINISH
!insertmacro MUI_LANGUAGE "TradChinese"
!insertmacro MUI_LANGUAGE "English"

LangString WelcomeText ${LANG_TRADCHINESE} "安裝 TransLamp 離線翻譯程式，隨附中英雙向語言包。支援 Windows 10 以上的 x64 電腦。$\r$\n$\r$\n請先關閉 TransLamp。程式安裝至目前使用者帳戶。若缺少適用的 VC++ x64 執行元件，將執行隨附的 Microsoft 安裝程式並要求管理員授權；不會自動重新啟動電腦。$\r$\n$\r$\n安裝過程無須網路。"
LangString WelcomeText ${LANG_ENGLISH} "Install TransLamp with both Chinese/English offline language packs. Requires Windows 10 or later on an x64 PC.$\r$\n$\r$\nClose TransLamp before continuing. The app is installed for the current user. If a suitable VC++ x64 runtime is missing, the included Microsoft installer will request administrator approval. Your PC will not restart automatically.$\r$\n$\r$\nNo internet connection is required."
LangString CoreSection ${LANG_TRADCHINESE} "TransLamp 程式與中英語言包 (必要)"
LangString CoreSection ${LANG_ENGLISH} "TransLamp and Chinese/English language packs (required)"
LangString DesktopSection ${LANG_TRADCHINESE} "建立桌面捷徑"
LangString DesktopSection ${LANG_ENGLISH} "Create a desktop shortcut"
LangString CoreDescription ${LANG_TRADCHINESE} "完整離線程式、內嵌 runtime、兩個語言包與開始功能表捷徑。"
LangString CoreDescription ${LANG_ENGLISH} "The complete offline app, embedded runtime, both language packs, and a Start menu shortcut."
LangString DesktopDescription ${LANG_TRADCHINESE} "在目前使用者的桌面建立 TransLamp 捷徑。"
LangString DesktopDescription ${LANG_ENGLISH} "Create a TransLamp shortcut on the current user's desktop."
LangString UnsupportedWindows ${LANG_TRADCHINESE} "TransLamp 需要 Windows 10 以上的原生 x64 電腦；此安裝程式不支援 x86 或 ARM64 電腦。"
LangString UnsupportedWindows ${LANG_ENGLISH} "TransLamp requires Windows 10 or later on a native x64 PC. This installer does not support x86 or ARM64 PCs."
LangString UnsafeDirectory ${LANG_TRADCHINESE} "無法使用此安裝位置。請選擇空資料夾，或目前使用者已登錄的 TransLamp 安裝資料夾。既有檔案已保留。"
LangString UnsafeDirectory ${LANG_ENGLISH} "This installation location cannot be used. Choose an empty folder or this user's registered TransLamp installation folder. Existing files were preserved."
LangString CloseApp ${LANG_TRADCHINESE} "TransLamp 程式檔目前無法寫入。請關閉 TransLamp 及其翻譯程序，確認資料夾可寫入後重試。安裝程式不會強制終止程序。"
LangString CloseApp ${LANG_ENGLISH} "The TransLamp executable cannot be written. Close TransLamp and its translation processes, check folder permissions, and try again. The installer will not terminate processes."
LangString VcAdminNotice ${LANG_TRADCHINESE} "需要安裝 Microsoft Visual C++ x64 執行元件 (至少 ${VC_MINIMUM_VERSION})。接下來將執行包內 Microsoft 安裝程式，並要求管理員授權。電腦不會自動重新啟動。"
LangString VcAdminNotice ${LANG_ENGLISH} "Microsoft Visual C++ x64 runtime ${VC_MINIMUM_VERSION} or later is required. The included Microsoft installer will now request administrator approval. Your PC will not restart automatically."
LangString VcInstalling ${LANG_TRADCHINESE} "正在安裝隨附的 Microsoft Visual C++ x64 執行元件…"
LangString VcInstalling ${LANG_ENGLISH} "Installing the included Microsoft Visual C++ x64 runtime..."
LangString VcFailed ${LANG_TRADCHINESE} "VC++ x64 執行元件未成功安裝或版本驗證未通過。TransLamp 安裝已停止。請確認管理員授權與 Microsoft 安裝結果後重試。"
LangString VcFailed ${LANG_ENGLISH} "The VC++ x64 runtime installation or version verification failed. TransLamp installation has stopped. Check administrator approval and the Microsoft installer result before trying again."
LangString VcRestart ${LANG_TRADCHINESE} "VC++ x64 執行元件版本已確認。若 Microsoft 安裝程式要求重新啟動，請自行重新啟動電腦後再使用 TransLamp；安裝程式不會自動重啟。"
LangString VcRestart ${LANG_ENGLISH} "The VC++ x64 runtime version was verified. If the Microsoft installer requests a restart, restart your PC manually before using TransLamp. This installer will not restart it automatically."
LangString InstallFailed ${LANG_TRADCHINESE} "安裝未完成，部分程式檔可能已寫入。請確認 TransLamp 已關閉、磁碟空間與資料夾權限，再重新安裝。"
LangString InstallFailed ${LANG_ENGLISH} "Installation did not complete; some app files may already have been written. Check that TransLamp is closed, disk space, and folder permissions before reinstalling."
LangString UninstallText ${LANG_TRADCHINESE} "將移除此安裝程式所提供的 TransLamp 程式檔與捷徑。$\r$\n$\r$\n已下載／匯入的語言包與偏好設定 (%LOCALAPPDATA%\ToolKeeper\TransLamp) 會保留，安裝資料夾內另外新增的檔案也會保留。請先關閉 TransLamp。"
LangString UninstallText ${LANG_ENGLISH} "Remove the TransLamp app files and shortcuts supplied by this installer.$\r$\n$\r$\nDownloaded/imported language packs and preferences in %LOCALAPPDATA%\ToolKeeper\TransLamp will be kept. Other files added to the installation folder will also be kept. Close TransLamp first."
LangString UnsafeUninstall ${LANG_TRADCHINESE} "無法確認此資料夾屬於目前使用者登錄的 TransLamp 安裝。解除安裝已停止，檔案已保留。"
LangString UnsafeUninstall ${LANG_ENGLISH} "This folder could not be verified as the current user's registered TransLamp installation. Uninstallation has stopped and files were preserved."
LangString UninstallFailed ${LANG_TRADCHINESE} "解除安裝未完成。請關閉 TransLamp 及其翻譯程序，確認資料夾權限後重試。若解除安裝程式已移除，請重新安裝 TransLamp 以修復。"
LangString UninstallFailed ${LANG_ENGLISH} "Uninstallation did not complete. Close TransLamp and its translation processes, check folder permissions, and try again. If the uninstaller was removed, reinstall TransLamp to repair it."

Function .onInit
  SetShellVarContext current
  SetRegView 64
  !insertmacro MUI_LANGDLL_DISPLAY
  ${IfNot} ${AtLeastWin10}
    Goto unsupported_windows
  ${EndIf}
  ; Native AMD64 is architecture 9. Do not promise ARM64 emulation support.
  System::Alloc 64
  Pop $0
  StrCmp $0 0 unsupported_windows
  System::Call 'kernel32::GetNativeSystemInfo(p r0)'
  System::Call '*$0(&i2 .r1)'
  System::Free $0
  StrCmp $1 9 0 unsupported_windows
  ReadRegStr $RegisteredInstallDir HKCU "${UNINSTALL_KEY}" "InstallLocation"
  StrCmp $RegisteredInstallDir "" init_done
  StrCpy $INSTDIR $RegisteredInstallDir
  init_done:
    Return
  unsupported_windows:
    MessageBox MB_OK|MB_ICONSTOP "$(UnsupportedWindows)" /SD IDOK
    SetErrorLevel 1
    Quit
FunctionEnd

Function ValidateInstallDirectory
  ; NSIS GetFullPathName requires an existing path. The Win32 API also handles
  ; a new destination, which is the normal first-install case.
  System::Call 'kernel32::GetFullPathNameW(w "$INSTDIR", i ${NSIS_MAX_STRLEN}, w .r2, p 0) i .r1'
  IntCmp $1 0 unsafe_directory unsafe_directory
  IntCmp $1 ${NSIS_MAX_STRLEN} unsafe_directory 0 unsafe_directory
  StrCpy $INSTDIR $2
  ${GetRoot} "$INSTDIR" $0
  StrCmp $0 "" unsafe_directory
  StrCmp $INSTDIR $0 unsafe_directory
  StrCmp $INSTDIR "$0\" unsafe_directory
  System::Call 'kernel32::GetFileAttributesW(w "$INSTDIR") i .r0'
  StrCmp $0 -1 directory_ok
  IntOp $1 $0 & 0x10
  StrCmp $1 0 unsafe_directory
  IntOp $1 $0 & 0x400
  StrCmp $1 0 0 unsafe_directory
  ClearErrors
  FindFirst $0 $1 "$INSTDIR\*"
  IfErrors directory_ok
  directory_entry:
    StrCmp $1 "" directory_empty
    StrCmp $1 "." directory_next
    StrCmp $1 ".." directory_next
    FindClose $0
    StrCmp $RegisteredInstallDir "" unsafe_directory
    GetFullPathName $2 "$RegisteredInstallDir"
    StrCmp $INSTDIR $2 0 unsafe_directory
    ReadINIStr $2 "$INSTDIR\${INSTALL_MARKER}" "TransLamp" "Product"
    StrCmp $2 "${PRODUCT_ID}" 0 unsafe_directory
    ReadINIStr $2 "$INSTDIR\${INSTALL_MARKER}" "TransLamp" "Scope"
    StrCmp $2 "current-user" directory_ok unsafe_directory
  directory_next:
    FindNext $0 $1
    Goto directory_entry
  directory_empty:
    FindClose $0
  directory_ok:
    Return
  unsafe_directory:
    MessageBox MB_OK|MB_ICONSTOP "$(UnsafeDirectory)" /SD IDOK
    SetErrorLevel 1
    Abort
FunctionEnd

; Refuse an in-use or unwritable executable; never kill the user's process.
!macro DefineCheckAppClosed PREFIX
Function ${PREFIX}CheckAppClosed
  IfFileExists "$INSTDIR\TransLamp.exe" 0 app_check_done
  System::Call 'kernel32::CreateFileW(w "$INSTDIR\TransLamp.exe", i 0x40000000, i 7, p 0, i 3, i 0, p 0) p .r0'
  StrCmp $0 -1 0 app_close_handle
  MessageBox MB_OK|MB_ICONSTOP "$(CloseApp)" /SD IDOK
  SetErrorLevel 1
  Abort
  app_close_handle:
    System::Call 'kernel32::CloseHandle(p r0)'
  app_check_done:
FunctionEnd
!macroend
!insertmacro DefineCheckAppClosed ""
!insertmacro DefineCheckAppClosed "un."

Function CheckVcRuntime
  StrCpy $VcRuntimeReady 0
  StrCpy $3 "64"
  SetRegView 64
  vc_check_view:
    ReadRegDWORD $0 HKLM "${VC_RUNTIME_KEY}" "Installed"
    StrCmp $0 1 0 vc_next_view
    ReadRegStr $1 HKLM "${VC_RUNTIME_KEY}" "Version"
    StrCmp $1 "" vc_next_view
    StrCpy $2 $1 1
    StrCmp $2 "v" 0 +2
    StrCpy $1 $1 "" 1
    ${VersionCompare} "$1" "${VC_MINIMUM_VERSION}" $2
    StrCmp $2 2 vc_next_view
    StrCpy $VcRuntimeReady 1
    Goto vc_check_done
  vc_next_view:
    StrCmp $3 "64" 0 vc_check_done
    StrCpy $3 "32"
    SetRegView 32
    Goto vc_check_view
  vc_check_done:
    SetRegView 64
FunctionEnd

Function EnsureVcRuntime
  Call CheckVcRuntime
  StrCmp $VcRuntimeReady 1 vc_ready
  MessageBox MB_OK|MB_ICONINFORMATION "$(VcAdminNotice)" /SD IDOK
  InitPluginsDir
  SetOutPath "$PLUGINSDIR"
  ClearErrors
  File /oname=VC_redist.x64.exe "${PAYLOAD_DIR}\Prerequisites\VC_redist.x64.exe"
  IfErrors vc_failed
  DetailPrint "$(VcInstalling)"
  ClearErrors
  ExecShellWait "runas" "$PLUGINSDIR\VC_redist.x64.exe" "/install /passive /norestart" SW_SHOWNORMAL
  IfErrors vc_failed
  Call CheckVcRuntime
  StrCmp $VcRuntimeReady 1 0 vc_failed
  MessageBox MB_OK|MB_ICONINFORMATION "$(VcRestart)" /SD IDOK
  Goto vc_ready
  vc_failed:
    MessageBox MB_OK|MB_ICONSTOP "$(VcFailed)" /SD IDOK
    SetErrorLevel 1
    Abort
  vc_ready:
FunctionEnd

Section "$(CoreSection)" SecCore
  SectionIn RO
  Call ValidateInstallDirectory
  Call CheckAppClosed
  Call EnsureVcRuntime
  SetOutPath "$INSTDIR"
  SetOverwrite on
  ClearErrors
  ; Establish ownership before extraction so a failed first extraction can be
  ; repaired. DisplayName is registered only after the complete payload is copied.
  WriteUninstaller "$INSTDIR\Uninstall.exe"
  WriteINIStr "$INSTDIR\${INSTALL_MARKER}" "TransLamp" "Product" "${PRODUCT_ID}"
  WriteINIStr "$INSTDIR\${INSTALL_MARKER}" "TransLamp" "Version" "${APP_VERSION}"
  WriteINIStr "$INSTDIR\${INSTALL_MARKER}" "TransLamp" "Scope" "current-user"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "InstallLocation" "$INSTDIR"
  IfErrors install_failed
  File /r "${PAYLOAD_DIR}\*"
  IfErrors install_failed
  SetOutPath "$INSTDIR"
  CreateDirectory "$SMPROGRAMS\ToolKeeper"
  CreateShortCut "$SMPROGRAMS\ToolKeeper\TransLamp.lnk" "$INSTDIR\TransLamp.exe" "" "$INSTDIR\TransLamp.exe" 0
  WriteRegStr HKCU "${UNINSTALL_KEY}" "DisplayName" "TransLamp"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "DisplayVersion" "${APP_VERSION}"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "Publisher" "ToolKeeper"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "DisplayIcon" "$INSTDIR\TransLamp.exe,0"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "UninstallString" '$\"$INSTDIR\Uninstall.exe$\"'
  WriteRegStr HKCU "${UNINSTALL_KEY}" "QuietUninstallString" '$\"$INSTDIR\Uninstall.exe$\" /S'
  WriteRegStr HKCU "${UNINSTALL_KEY}" "InstallerLanguage" "$LANGUAGE"
  WriteRegDWORD HKCU "${UNINSTALL_KEY}" "EstimatedSize" ${INSTALLED_SIZE_KB}
  WriteRegDWORD HKCU "${UNINSTALL_KEY}" "NoModify" 1
  WriteRegDWORD HKCU "${UNINSTALL_KEY}" "NoRepair" 1
  IfErrors install_failed
  Goto install_done
  install_failed:
    MessageBox MB_OK|MB_ICONSTOP "$(InstallFailed)" /SD IDOK
    SetErrorLevel 1
    Abort
  install_done:
SectionEnd

Section "$(DesktopSection)" SecDesktop
  ClearErrors
  CreateShortCut "$DESKTOP\TransLamp.lnk" "$INSTDIR\TransLamp.exe" "" "$INSTDIR\TransLamp.exe" 0
  ${If} ${Errors}
    MessageBox MB_OK|MB_ICONSTOP "$(InstallFailed)" /SD IDOK
    SetErrorLevel 1
    Abort
  ${EndIf}
SectionEnd

!insertmacro MUI_FUNCTION_DESCRIPTION_BEGIN
  !insertmacro MUI_DESCRIPTION_TEXT ${SecCore} "$(CoreDescription)"
  !insertmacro MUI_DESCRIPTION_TEXT ${SecDesktop} "$(DesktopDescription)"
!insertmacro MUI_FUNCTION_DESCRIPTION_END

Function un.onInit
  SetShellVarContext current
  SetRegView 64
  !insertmacro MUI_UNGETLANGUAGE
  ReadRegStr $RegisteredInstallDir HKCU "${UNINSTALL_KEY}" "InstallLocation"
  StrCmp $RegisteredInstallDir "" unsafe_uninstall
  GetFullPathName $0 "$RegisteredInstallDir"
  GetFullPathName $INSTDIR "$INSTDIR"
  StrCmp $INSTDIR $0 0 unsafe_uninstall
  ReadINIStr $0 "$INSTDIR\${INSTALL_MARKER}" "TransLamp" "Product"
  StrCmp $0 "${PRODUCT_ID}" 0 unsafe_uninstall
  ReadINIStr $0 "$INSTDIR\${INSTALL_MARKER}" "TransLamp" "Scope"
  StrCmp $0 "current-user" 0 unsafe_uninstall
  System::Call 'kernel32::GetFileAttributesW(w "$INSTDIR") i .r0'
  IntOp $0 $0 & 0x400
  StrCmp $0 0 un_init_done
  unsafe_uninstall:
    MessageBox MB_OK|MB_ICONSTOP "$(UnsafeUninstall)" /SD IDOK
    SetErrorLevel 1
    Quit
  un_init_done:
FunctionEnd

Section "Uninstall"
  Call un.CheckAppClosed
  StrCpy $UninstallDeleteFailed 0
  ; Generated from this build's payload: exact Delete commands followed by
  ; deepest-first, nonrecursive RMDir commands. Each Delete records errors in
  ; $UninstallDeleteFailed; nonempty folders are intentionally preserved.
  !include "${UNINSTALL_FILES}"
  StrCmp $UninstallDeleteFailed 0 0 uninstall_failed
  ClearErrors
  Delete "$SMPROGRAMS\ToolKeeper\TransLamp.lnk"
  Delete "$DESKTOP\TransLamp.lnk"
  IfErrors uninstall_failed
  ; Keep the ownership marker and registry while removal of the uninstaller
  ; could still fail, allowing retry when its executable was locked.
  Delete "$INSTDIR\Uninstall.exe"
  IfErrors uninstall_failed
  Delete "$INSTDIR\${INSTALL_MARKER}"
  IfErrors uninstall_failed
  DeleteRegKey HKCU "${UNINSTALL_KEY}"
  IfErrors uninstall_failed
  ; These can remain nonempty because of another ToolKeeper app or user files.
  RMDir "$SMPROGRAMS\ToolKeeper"
  RMDir "$INSTDIR"
  Goto uninstall_done
  uninstall_failed:
    MessageBox MB_OK|MB_ICONSTOP "$(UninstallFailed)" /SD IDOK
    SetErrorLevel 1
    Abort
  uninstall_done:
SectionEnd
