# Быстрая сборка Android

## Через интерфейс

1. После клонирования выполните `python3 Tools/generate_assets.py`.
2. Откройте проект в Unity 2022.3 LTS.
3. Убедитесь, что в Unity Hub установлен модуль Android Build Support вместе с SDK, NDK и OpenJDK.
4. Выберите `Ballistic Sniper → Configure Project`.
5. Выберите `Ballistic Sniper → Build Android APK`.

APK будет сохранён в `Builds/Android/Ballistic-Sniper-Unity-v3.3.0.apk`.

## Через командную строку

Linux/macOS:

```bash
"/path/to/Unity" -batchmode -quit \
  -projectPath "/absolute/path/to/BallisticSniperUnity" \
  -executeMethod BallisticSniper.Editor.ProjectConfigurator.BuildAndroidApk \
  -logFile "Builds/unity-build.log"
```

Windows PowerShell:

```powershell
& "C:\Program Files\Unity\Hub\Editor\2022.3.62f1\Editor\Unity.exe" `
  -batchmode -quit `
  -projectPath "C:\path\to\BallisticSniperUnity" `
  -executeMethod BallisticSniper.Editor.ProjectConfigurator.BuildAndroidApk `
  -logFile "Builds\unity-build.log"
```

Если установлен другой патч Unity 2022.3 LTS или Unity 6, редактор предложит безопасно обновить проект. После обновления сначала нажмите Play и затем собирайте APK.
