; Instalador do ProjectPlanner para Windows (NSIS 3). Quem o gera é o scripts/publicar.sh (ver PUBLISHING.md):
; O arquivo é UTF-8 com BOM, e o makensis precisa de um locale UTF-8 para ler os acentos (o publicar.sh o define).
;
;   makensis -DVERSAO=0.15.0 -DARQUITETURA=x64 -DEXE=<.exe publicado> \
;            -DICONE_APP=<ProjectPlanner.ico> -DICONE_ARQUIVO=<ProjetoPplan.ico> -DSAIDA=<instalador.exe> ProjectPlanner.nsi
;
; Instala só para o usuário atual, sem pedir administrador, em %LOCALAPPDATA%\Programs\ProjectPlanner:
;   - ProjectPlanner.exe e o ícone dos arquivos de projeto (ProjetoPplan.ico);
;   - atalho no menu Iniciar;
;   - a associação dos arquivos .pplan (duplo clique abre o projeto no ProjectPlanner, com ícone próprio);
;   - o desinstalador, listado em Configurações > Aplicativos, que desfaz tudo isso.
; Tudo no registro fica em HKEY_CURRENT_USER, que o usuário pode alterar sem permissões especiais.

Unicode true
SetCompressor /SOLID lzma
RequestExecutionLevel user

!include "MUI2.nsh"
!include "FileFunc.nsh"

!define NOME "ProjectPlanner"
!define PROGID "ProjectPlanner.Projeto"
!define EXTENSAO ".pplan"
!define DESINSTALAR "Software\Microsoft\Windows\CurrentVersion\Uninstall\ProjectPlanner"
; SHChangeNotify(SHCNE_ASSOCCHANGED): o Explorer relê as associações e os ícones na hora.
!define AVISAR_O_EXPLORER "System::Call 'shell32::SHChangeNotify(i 0x08000000, i 0, p 0, p 0)'"

Name "${NOME}"
Caption "Instalar o ${NOME} ${VERSAO}"
OutFile "${SAIDA}"
InstallDir "$LOCALAPPDATA\Programs\${NOME}"
BrandingText "${NOME} ${VERSAO} (${ARQUITETURA})"

!define MUI_ICON "${ICONE_APP}"
!define MUI_UNICON "${ICONE_APP}"
!define MUI_FINISHPAGE_RUN "$INSTDIR\ProjectPlanner.exe"
!define MUI_FINISHPAGE_RUN_TEXT "Abrir o ${NOME}"

!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "PortugueseBR"

; Propriedades do arquivo do instalador (Propriedades > Detalhes).
VIProductVersion "${VERSAO}.0"
VIAddVersionKey /LANG=${LANG_PORTUGUESEBR} "ProductName" "${NOME}"
VIAddVersionKey /LANG=${LANG_PORTUGUESEBR} "ProductVersion" "${VERSAO}"
VIAddVersionKey /LANG=${LANG_PORTUGUESEBR} "FileVersion" "${VERSAO}"
VIAddVersionKey /LANG=${LANG_PORTUGUESEBR} "FileDescription" "Instalador do ${NOME}"
VIAddVersionKey /LANG=${LANG_PORTUGUESEBR} "CompanyName" "gustavodossantosqueiroz"
VIAddVersionKey /LANG=${LANG_PORTUGUESEBR} "LegalCopyright" "© 2026 gustavodossantosqueiroz"

Section "${NOME}"
  SetOutPath "$INSTDIR"
  File "/oname=ProjectPlanner.exe" "${EXE}"
  File "/oname=ProjetoPplan.ico" "${ICONE_ARQUIVO}"
  WriteUninstaller "$INSTDIR\Desinstalar.exe"
  CreateShortcut "$SMPROGRAMS\${NOME}.lnk" "$INSTDIR\ProjectPlanner.exe"

  ; Arquivos .pplan: tipo, ícone e o comando que abre o projeto ("%1" é o arquivo clicado).
  WriteRegStr HKCU "Software\Classes\${EXTENSAO}" "" "${PROGID}"
  WriteRegStr HKCU "Software\Classes\${EXTENSAO}\OpenWithProgids" "${PROGID}" ""
  WriteRegStr HKCU "Software\Classes\${PROGID}" "" "Projeto do ${NOME}"
  WriteRegStr HKCU "Software\Classes\${PROGID}\DefaultIcon" "" "$INSTDIR\ProjetoPplan.ico"
  WriteRegStr HKCU "Software\Classes\${PROGID}\shell\open\command" "" '"$INSTDIR\ProjectPlanner.exe" "%1"'

  ; Desinstalador em Configurações > Aplicativos.
  ${GetSize} "$INSTDIR" "/S=0K" $0 $1 $2
  WriteRegStr HKCU "${DESINSTALAR}" "DisplayName" "${NOME}"
  WriteRegStr HKCU "${DESINSTALAR}" "DisplayVersion" "${VERSAO}"
  WriteRegStr HKCU "${DESINSTALAR}" "Publisher" "gustavodossantosqueiroz"
  WriteRegStr HKCU "${DESINSTALAR}" "DisplayIcon" "$INSTDIR\ProjectPlanner.exe"
  WriteRegStr HKCU "${DESINSTALAR}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "${DESINSTALAR}" "UninstallString" '"$INSTDIR\Desinstalar.exe"'
  WriteRegStr HKCU "${DESINSTALAR}" "QuietUninstallString" '"$INSTDIR\Desinstalar.exe" /S'
  WriteRegDWORD HKCU "${DESINSTALAR}" "EstimatedSize" $0
  WriteRegDWORD HKCU "${DESINSTALAR}" "NoModify" 1
  WriteRegDWORD HKCU "${DESINSTALAR}" "NoRepair" 1

  ${AVISAR_O_EXPLORER}
SectionEnd

Section "Uninstall"
  Delete "$INSTDIR\ProjectPlanner.exe"
  Delete "$INSTDIR\ProjetoPplan.ico"
  Delete "$INSTDIR\Desinstalar.exe"
  RMDir "$INSTDIR"
  Delete "$SMPROGRAMS\${NOME}.lnk"

  DeleteRegKey HKCU "Software\Classes\${PROGID}"
  DeleteRegValue HKCU "Software\Classes\${EXTENSAO}\OpenWithProgids" "${PROGID}"
  ; A extensão só sai se ainda for nossa (outro programa pode tê-la assumido depois).
  ReadRegStr $0 HKCU "Software\Classes\${EXTENSAO}" ""
  StrCmp $0 "${PROGID}" 0 +2
    DeleteRegValue HKCU "Software\Classes\${EXTENSAO}" ""
  DeleteRegKey /ifempty HKCU "Software\Classes\${EXTENSAO}\OpenWithProgids"
  DeleteRegKey /ifempty HKCU "Software\Classes\${EXTENSAO}"
  DeleteRegKey HKCU "${DESINSTALAR}"

  ${AVISAR_O_EXPLORER}
SectionEnd
