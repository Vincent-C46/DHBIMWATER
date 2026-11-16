; DHBIMWATER Revit 2025-2026 Addin Installer
; Installs DHBIMWATER only. Does not install DH-Water.

!include "MUI2.nsh"

;--------------------------------
; General
Name "DHBIMWATER for Revit 2025-2026"
OutFile "C:\BuildOutput\DHBIMWATER\DHBIMWATER_25~26.exe"
Unicode True

InstallDir "C:\ProgramData\Autodesk\Revit\Addins\DHBIMWATER_25~26"

!define BUILD_OUTPUT_DIR "C:\BuildOutput\DHBIMWATER"
!define REVIT_2025_ADDINS_PATH "C:\ProgramData\Autodesk\Revit\Addins\2025"
!define REVIT_2026_ADDINS_PATH "C:\ProgramData\Autodesk\Revit\Addins\2026"
!define REVIT_2025_INSTALL_DIR "${REVIT_2025_ADDINS_PATH}\DHBIMWATER"
!define REVIT_2026_INSTALL_DIR "${REVIT_2026_ADDINS_PATH}\DHBIMWATER"
!define UNINSTALL_REG_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\DHBIMWATER_25~26"
; 프로젝트 템플릿(docs/324). .rte는 git 밖 — 빌드 전 이 폴더에 복사. 파일 이름 = Revit 목록 표시 이름
!define TEMPLATE_SOURCE_DIR "${BUILD_OUTPUT_DIR}\Templates"
!define TEMPLATE_SCRIPT "Register-RevitTemplates.ps1"
!define POWERSHELL "$SYSDIR\WindowsPowerShell\v1.0\powershell.exe"

RequestExecutionLevel user

;--------------------------------
; Interface Settings
!define MUI_ABORTWARNING
!define MUI_ICON "${NSISDIR}\Contrib\Graphics\Icons\modern-install.ico"
!define MUI_UNICON "${NSISDIR}\Contrib\Graphics\Icons\modern-uninstall.ico"

;--------------------------------
; Pages
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_LICENSE "License.txt"
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

;--------------------------------
; Languages
!insertmacro MUI_LANGUAGE "Korean"

;--------------------------------
; Revit 켜져 있으면 중단 — Revit이 종료 때 Revit.ini 덮어써서 템플릿 등록 사라짐
!macro DEFINE_CHECK_REVIT_CLOSED PREFIX
Function ${PREFIX}CheckRevitClosed
    retry:
        nsExec::ExecToStack '"$SYSDIR\cmd.exe" /c tasklist /FI "IMAGENAME eq Revit.exe" /NH | "$SYSDIR\find.exe" /I "Revit.exe"'
        Pop $0
        Pop $1
        StrCmp $0 "0" 0 done
        MessageBox MB_RETRYCANCEL|MB_ICONEXCLAMATION "Revit이 실행 중입니다.$\r$\nRevit을 모두 종료한 뒤 [다시 시도]를 누르세요.$\r$\n(Revit이 켜져 있으면 템플릿 등록이 지워집니다)" IDRETRY retry
        Abort
    done:
FunctionEnd
!macroend
!insertmacro DEFINE_CHECK_REVIT_CLOSED ""
!insertmacro DEFINE_CHECK_REVIT_CLOSED "un."

Function .onInit
    Call CheckRevitClosed
FunctionEnd

Function un.onInit
    Call un.CheckRevitClosed
FunctionEnd

; 버전별 템플릿 복사 + Revit.ini 등록. 그 버전 Revit 없으면 건너뜀
!macro INSTALL_TEMPLATES VER
    IfFileExists "C:\ProgramData\Autodesk\RVT ${VER}\*.*" 0 tpl_skip_${VER}
        ; 옛 설치 위치(Templates\DH) 정리 — 목록 항목은 같은 이름이라 새 경로로 바뀜
        Delete "C:\ProgramData\Autodesk\RVT ${VER}\Templates\DH\*.rte"
        RMDir "C:\ProgramData\Autodesk\RVT ${VER}\Templates\DH"
        CreateDirectory "C:\ProgramData\Autodesk\RVT ${VER}\Templates\Korean\DH"
        ; 이전 버전 파일 정리 — 옛 이름 항목이 깨진 경로로 안 남게
        Delete "C:\ProgramData\Autodesk\RVT ${VER}\Templates\Korean\DH\*.rte"
        CopyFiles /SILENT "$PLUGINSDIR\Templates\*.rte" "C:\ProgramData\Autodesk\RVT ${VER}\Templates\Korean\DH"
        nsExec::ExecToLog '"${POWERSHELL}" -NoProfile -ExecutionPolicy Bypass -File "$INSTDIR\${TEMPLATE_SCRIPT}" -Action Register -RevitVersion ${VER} -TemplateDir "C:\ProgramData\Autodesk\RVT ${VER}\Templates\Korean\DH"'
        Pop $0
        StrCmp $0 "0" tpl_skip_${VER}
        MessageBox MB_OK|MB_ICONEXCLAMATION "Revit ${VER} 템플릿 파일은 설치했지만 템플릿 목록 등록에 실패했습니다.$\r$\nRevit 옵션 > 파일 위치에서 직접 추가하세요:$\r$\nC:\ProgramData\Autodesk\RVT ${VER}\Templates\Korean\DH"
    tpl_skip_${VER}:
!macroend

; 버전별 등록 해제 + 파일 삭제. DH 폴더 경로 항목만 뺌
!macro UNINSTALL_TEMPLATES VER
    IfFileExists "C:\ProgramData\Autodesk\RVT ${VER}\Templates\Korean\DH\*.*" 0 untpl_skip_${VER}
        nsExec::ExecToLog '"${POWERSHELL}" -NoProfile -ExecutionPolicy Bypass -File "$INSTDIR\${TEMPLATE_SCRIPT}" -Action Unregister -RevitVersion ${VER} -TemplateDir "C:\ProgramData\Autodesk\RVT ${VER}\Templates\Korean\DH"'
        Pop $0
        Delete "C:\ProgramData\Autodesk\RVT ${VER}\Templates\Korean\DH\*.rte"
        RMDir "C:\ProgramData\Autodesk\RVT ${VER}\Templates\Korean\DH"
    untpl_skip_${VER}:
!macroend

;--------------------------------
; Version Information
VIProductVersion "1.0.0.0"
VIAddVersionKey "ProductName" "DHBIMWATER"
VIAddVersionKey "CompanyName" "DH Corporation"
VIAddVersionKey "FileDescription" "DHBIMWATER Revit 2025-2026 Addin"
VIAddVersionKey "FileVersion" "1.0.0.0"
VIAddVersionKey "LegalCopyright" "Copyright (C) 2026 DH Corporation"

;--------------------------------
; Installer Sections

Section "Install" SecInstall
    ; Revit 2025
    SetOutPath "${REVIT_2025_INSTALL_DIR}"
    File "${BUILD_OUTPUT_DIR}\DHBIMWATER.Revit.dll"
    File "${BUILD_OUTPUT_DIR}\DidasUsage.dll"
    File "${BUILD_OUTPUT_DIR}\DidasReader.dll"

    SetOutPath "${REVIT_2025_ADDINS_PATH}"
    Delete "${REVIT_2025_ADDINS_PATH}\DHBIMWATER.addin"
    Delete "${REVIT_2025_ADDINS_PATH}\02_DHBIMWATER.addin"
    File /oname=DHBIMWATER.addin "${BUILD_OUTPUT_DIR}\DHBIMWATER.addin"

    ; Revit 2026
    SetOutPath "${REVIT_2026_INSTALL_DIR}"
    File "${BUILD_OUTPUT_DIR}\DHBIMWATER.Revit.dll"
    File "${BUILD_OUTPUT_DIR}\DidasUsage.dll"
    File "${BUILD_OUTPUT_DIR}\DidasReader.dll"

    SetOutPath "${REVIT_2026_ADDINS_PATH}"
    Delete "${REVIT_2026_ADDINS_PATH}\DHBIMWATER.addin"
    Delete "${REVIT_2026_ADDINS_PATH}\02_DHBIMWATER.addin"
    File /oname=DHBIMWATER.addin "${BUILD_OUTPUT_DIR}\DHBIMWATER.addin"

    ; Shared uninstaller
    SetOutPath "$INSTDIR"
    WriteUninstaller "$INSTDIR\Uninstall.exe"

    ; 프로젝트 템플릿 — 스크립트는 제거 때도 써서 $INSTDIR에 둠, .rte는 한 번만 풀고 버전별 복사
    File "scripts\${TEMPLATE_SCRIPT}"
    InitPluginsDir
    SetOutPath "$PLUGINSDIR\Templates"
    File "${TEMPLATE_SOURCE_DIR}\*.rte"
    !insertmacro INSTALL_TEMPLATES 2025
    !insertmacro INSTALL_TEMPLATES 2026
    SetOutPath "$INSTDIR"

    ; Write registry keys for Add/Remove Programs
    WriteRegStr HKCU "${UNINSTALL_REG_KEY}" "DisplayName" "DHBIMWATER for Revit 2025-2026"
    WriteRegStr HKCU "${UNINSTALL_REG_KEY}" "UninstallString" "$INSTDIR\Uninstall.exe"
    WriteRegStr HKCU "${UNINSTALL_REG_KEY}" "DisplayIcon" "${REVIT_2026_INSTALL_DIR}\DHBIMWATER.Revit.dll"
    WriteRegStr HKCU "${UNINSTALL_REG_KEY}" "Publisher" "DH Corporation"
    WriteRegStr HKCU "${UNINSTALL_REG_KEY}" "DisplayVersion" "1.0.0"
    WriteRegDWORD HKCU "${UNINSTALL_REG_KEY}" "NoModify" 1
    WriteRegDWORD HKCU "${UNINSTALL_REG_KEY}" "NoRepair" 1
SectionEnd

;--------------------------------
; Uninstaller Section

Section "Uninstall"
    ; 프로젝트 템플릿 등록 해제 — 스크립트 지우기 전에
    !insertmacro UNINSTALL_TEMPLATES 2025
    !insertmacro UNINSTALL_TEMPLATES 2026
    Delete "$INSTDIR\${TEMPLATE_SCRIPT}"

    ; Revit 2025
    Delete "${REVIT_2025_INSTALL_DIR}\DHBIMWATER.Revit.dll"
    Delete "${REVIT_2025_INSTALL_DIR}\DidasUsage.dll"
    Delete "${REVIT_2025_INSTALL_DIR}\DidasReader.dll"
    Delete "${REVIT_2025_ADDINS_PATH}\DHBIMWATER.addin"
    Delete "${REVIT_2025_ADDINS_PATH}\02_DHBIMWATER.addin"
    RMDir "${REVIT_2025_INSTALL_DIR}"

    ; Revit 2026
    Delete "${REVIT_2026_INSTALL_DIR}\DHBIMWATER.Revit.dll"
    Delete "${REVIT_2026_INSTALL_DIR}\DidasUsage.dll"
    Delete "${REVIT_2026_INSTALL_DIR}\DidasReader.dll"
    Delete "${REVIT_2026_ADDINS_PATH}\DHBIMWATER.addin"
    Delete "${REVIT_2026_ADDINS_PATH}\02_DHBIMWATER.addin"
    RMDir "${REVIT_2026_INSTALL_DIR}"

    ; Shared uninstaller
    Delete "$INSTDIR\Uninstall.exe"
    RMDir "$INSTDIR"

    DeleteRegKey HKCU "${UNINSTALL_REG_KEY}"
SectionEnd
