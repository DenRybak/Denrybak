# Ballistic Sniper — Unity 3D

Автономная мобильная 3D-игра для Android на Unity 2022.3 LTS. Текущая версия: **5.0.0-unity**.

Версия 5.0 делает **МИССИИ** основным режимом, а **ТРЕНИРОВКУ** оставляет отдельным полигоном. Миссионные сцены теперь используют объёмных процедурных персонажей, городское окружение, движение NPC, раздельные зоны взаимодействия и физический ragdoll.

Android package ID остаётся `com.denis.ballisticsniper.unity`. VersionCode: **11**.

## v5.0

- 5 миссий и отдельный тренировочный полигон;
- briefing перед миссией;
- финальная миссия на 1000 м: офицер среди группы солдат;
- переменный FFP-зум от 8× до 100×;
- несколько движущихся NPC в сцене;
- объёмные procedural mesh-персонажи вместо billboard и видимых примитивов;
- Animator-host и процедурные idle/walk/gesture движения;
- отдельные зоны головы, корпуса, рук и ног;
- jointed ragdoll;
- городская 3D-среда: дорога, тротуары, здания, окна, крыши, машины, фонари и деревья;
- отдельное атмосферное небо для миссий;
- у тренировочных объектов убраны контрастные прямоугольные backplate/rim.

## Сборка APK

Unity: `Ballistic Sniper → Build Android APK`

Готовый файл:

`Builds/Android/Ballistic-Sniper-Unity-v5.0.0.apk`

GitHub Actions выполняет проверки проекта, Unity EditMode/PlayMode tests, Android build и runtime smoke-test в эмуляторе.

## Требования

- Unity 2022.3.62f1
- Android API 26+
- ARMv7 / ARM64 / x86_64
- IL2CPP
- landscape

## Проверка

```bash
python3 Tests/verify_project.py
```

Подробности: `Docs/FEATURE_MATRIX.md`.
