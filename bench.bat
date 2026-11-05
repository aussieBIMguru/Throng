@echo off
rem Throng stadium bench (round 025, D-380, D-405): report only, takes a few minutes. Paste the output back.
set THRONG_BENCH=1
dotnet test "%~dp0tests\Throng.Sim.Tests" -c Release --filter "TestCategory=Bench" --logger "console;verbosity=detailed"
set THRONG_BENCH=
pause
