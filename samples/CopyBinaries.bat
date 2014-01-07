copy ..\SimpleBatch\bin\Debug\SimpleBatch.dll binaries\SimpleBatch.dll

copy ..\Publish\bin\debug\* binaries\tools\*
del Binaries\Tools\*.vshost*
del Binaries\Tools\*.pdb
