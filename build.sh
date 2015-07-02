NUGET="build/tools/nuget/nuget.exe"
FAKE="build/tools/FAKE/tools/FAKE.exe"
XUNIT="build/tools/xunit.runner.console/tools/xunit.console.exe"
FSHARPCLI="build/tools/Fsharp.Formatting.CommandTool/Fsharp.Formatting.CommandTool.nupkg"

#we need nuget to install tools locally
if [[ ! -f "$NUGET" ]]; then
   echo NUGET not found.. Download...
   mkdir -p build/tools/nuget
   curl -o $NUGET https://api.nuget.org/downloads/nuget.exe
fi

#we need FAKE to process our build scripts
if [[ ! -f "$FAKE" ]]; then
    echo  FAKE not found.. Installing..
    mono --runtime=v4.0 "$NUGET" "install" "FAKE" "-OutputDirectory" "build/tools" "-ExcludeVersion" "-Prerelease"
fi

#workaround assembly resolution issues in build.fsx
export FSHARPI=`which fsharpi`
cat - > fsharpi <<"EOF"
#!/bin/bash
libdir=$PWD/build/tools/FAKE/tools/
$FSHARPI --lib:$libdir $@
EOF
chmod +x fsharpi
mono -V
mono --runtime=v4.5 "$FAKE" build/scripts/Targets.fsx $@ "skiptests=1" 
MONOEXIT=$?
rm fsharpi
#FORCE exit code to be that of calling fake not the last rm action
exit $MONOEXIT
