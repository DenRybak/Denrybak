# Быстрая сборка Android

## Через интерфейс

1. Выполните `python3 Tools/generate_assets.py`.
2. Откройте проект в Unity 2022.3 LTS.
3. Убедитесь, что установлен Android Build Support с SDK, NDK и OpenJDK.
4. Выберите `Ballistic Sniper → Configure Project`.
5. Выберите `Ballistic Sniper → Build Android APK`.

APK v5 будет сохранён в:

`Builds/Android/Ballistic-Sniper-Unity-v5.0.0.apk`

## Через командную строку

```bash
"/path/to/Unity" -batchmode -quit \
  -projectPath "/absolute/path/to/BallisticSniperUnity" \
  -executeMethod BallisticSniper.Editor.ProjectConfigurator.BuildAndroidApk \
  -logFile "Builds/unity-build.log"
```

Проект рассчитан на Unity 2022.3.62f1. Android package ID: `com.denis.ballisticsniper.unity`, versionCode: 11.
