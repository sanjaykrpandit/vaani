@echo off
echo ========================================
echo   VB Cable Volume Quick Fix
echo ========================================
echo.
echo Opening Windows Sound Control Panel...
echo.
echo INSTRUCTIONS:
echo.
echo Recording Tab:
echo   1. Double-click "CABLE-A Output"
echo   2. Go to "Levels" tab
echo   3. Set volume slider to 100%%
echo   4. Click Apply
echo   5. Do same for "CABLE-B Output"
echo.
echo Playback Tab:
echo   1. Double-click "CABLE-A Input"
echo   2. Go to "Levels" tab
echo   3. Set volume slider to 100%%
echo   4. Click Apply
echo   5. Do same for "CABLE-B Input"
echo.
echo Click OK when done, then run audio test again.
echo.
pause

start control mmsys.cpl sounds

echo.
echo Sound Control Panel opened!
echo Follow the instructions above.
echo.
pause
