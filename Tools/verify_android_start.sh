#!/usr/bin/env bash
set -euo pipefail

APK_PATH="${1:?APK path is required}"
PACKAGE_NAME="${2:?Android package name is required}"
RESULTS_DIR="${3:-TestResults}"

mkdir -p "$RESULTS_DIR"
test -s "$APK_PATH"
adb wait-for-device

for _ in $(seq 1 120); do
  if [ "$(adb shell getprop sys.boot_completed | tr -d '\r')" = "1" ]; then
    break
  fi
  sleep 1
done
test "$(adb shell getprop sys.boot_completed | tr -d '\r')" = "1"

adb install -r "$APK_PATH" | tee "$RESULTS_DIR/android-install.txt"
adb logcat -c
adb shell am force-stop "$PACKAGE_NAME"
adb shell monkey -p "$PACKAGE_NAME" -c android.intent.category.LAUNCHER 1 \
  | tee "$RESULTS_DIR/android-launch.txt"

menu_ready=0
for _ in $(seq 1 120); do
  adb logcat -d > "$RESULTS_DIR/android-logcat.txt"
  if grep -Fq "BALLISTIC_ANDROID_MENU_READY version=3.2.0 screen=Menu" "$RESULTS_DIR/android-logcat.txt"; then
    menu_ready=1
    break
  fi
  sleep 1
done
test "$menu_ready" -eq 1
cp "$RESULTS_DIR/android-logcat.txt" "$RESULTS_DIR/android-menu-logcat.txt"
app_pid="$(adb shell pidof "$PACKAGE_NAME" | tr -d '\r')"
test -n "$app_pid"

adb exec-out screencap -p > "$RESULTS_DIR/android-menu-before-tap.png"
read -r screen_width screen_height < <(
  python3 -c 'import struct,sys; data=open(sys.argv[1], "rb").read(24); print(*struct.unpack(">II", data[16:24]))' \
    "$RESULTS_DIR/android-menu-before-tap.png"
)
test "$screen_width" -gt "$screen_height"

# The START button occupies x=0.72..0.93 and y=0.51..0.66 in Unity's
# bottom-left coordinate system. adb uses a top-left origin.
tap_x=$((screen_width * 825 / 1000))
tap_y=$((screen_height * 415 / 1000))
adb logcat -c
adb shell input tap "$tap_x" "$tap_y"

started=0
for _ in $(seq 1 60); do
  adb logcat -d > "$RESULTS_DIR/android-logcat.txt"
  if grep -Fq "BALLISTIC_ANDROID_START_OK screen=Playing menuVisible=False gameplayVisible=True scopeVisible=True" \
      "$RESULTS_DIR/android-logcat.txt"; then
    started=1
    break
  fi
  sleep 1
done

adb exec-out screencap -p > "$RESULTS_DIR/android-gameplay-after-tap.png"
adb shell dumpsys activity activities > "$RESULTS_DIR/android-activity.txt"
adb logcat -d --pid="$app_pid" > "$RESULTS_DIR/android-app-logcat.txt"
test "$started" -eq 1
grep -Fq "$PACKAGE_NAME" "$RESULTS_DIR/android-activity.txt"
if grep -Eq "FATAL EXCEPTION|NullReferenceException|MissingReferenceException" "$RESULTS_DIR/android-app-logcat.txt"; then
  echo "Android runtime exception detected" >&2
  exit 1
fi

echo "Installed APK passed the real Android START-tap test at ${screen_width}x${screen_height}."
