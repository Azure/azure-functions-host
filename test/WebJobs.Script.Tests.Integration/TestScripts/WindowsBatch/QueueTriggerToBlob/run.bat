echo OFF
SET /p input=<%input%
echo Windows Batch script processed queue message '%input%'
echo %input% > %output%