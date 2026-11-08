@echo off

call "%~dp0.venv\Scripts\activate"


set PATH=C:\Program Files\Prophesee\bin;C:\Program Files\Prophesee\third_party\bin;%PATH%
set PYTHONPATH=C:\Program Files\Prophesee\lib\python3\site-packages;%PYTHONPATH%
set MV_HAL_PLUGIN_SEARCH_MODE=PLUGIN_PATH_ONLY
set MV_HAL_PLUGIN_PATH=C:\Program Files\CenturyArks\plugins
set MV_HAL_DEBUG_PLUGIN=1

echo Environment ready.
echo.
python -c "from metavision_core.event_io import EventsIterator; print('metavision_core OK')"
echo.
metavision_platform_info
echo.

cmd /k