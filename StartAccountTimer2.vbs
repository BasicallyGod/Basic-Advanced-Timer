' Launches Basic's Advanced Timer (AccountTimer2.ps1) without a console.
' Keep this file in the same folder as AccountTimer2.ps1.
Set fso = CreateObject("Scripting.FileSystemObject")
folder = fso.GetParentFolderName(WScript.ScriptFullName)
CreateObject("WScript.Shell").Run "powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File """ & folder & "\AccountTimer2.ps1""", 0, False
