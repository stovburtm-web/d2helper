Option Explicit

Dim shell, fso, repoDir, cmd
Set shell = CreateObject("WScript.Shell")
Set fso = CreateObject("Scripting.FileSystemObject")

repoDir = fso.GetParentFolderName(WScript.ScriptFullName)
cmd = "cmd /c """ & repoDir & "\start-d2helper.cmd"""

' 0 = hidden window, False = do not wait
shell.Run cmd, 0, False
