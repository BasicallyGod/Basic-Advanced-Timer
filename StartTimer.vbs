' Launches BillableTimer.ps1 silently (no console window).
' Keep this file in the same folder as BillableTimer.ps1.
Set fso = CreateObject("Scripting.FileSystemObject")
folder = fso.GetParentFolderName(WScript.ScriptFullName)
CreateObject("WScript.Shell").Run "powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File """ & folder & "\BillableTimer.ps1""", 0, False
