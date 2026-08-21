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
adb shell settings put secure immersive_mode_confirmations confirmed >/dev/null 2>&1 || true

adb install -r "$APK_PATH" | tee "$RESULTS_DIR/android-install.txt"
adb logcat -c
adb shell am force-stop "$PACKAGE_NAME"
adb shell monkey -p "$PACKAGE_NAME" -c android.intent.category.LAUNCHER 1 \
  | tee "$RESULTS_DIR/android-launch.txt"

app_pid=""
for _ in $(seq 1 30); do
  # pidof exits with status 1 during the short Activity-start race. Keep the
  # retry loop alive under set -e until Android publishes the process.
  app_pid="$(adb shell pidof "$PACKAGE_NAME" 2>/dev/null | tr -d '\r' || true)"
  if [ -n "$app_pid" ]; then
    break
  fi
  sleep 1
done
test -n "$app_pid"

menu_ready=0
for _ in $(seq 1 120); do
  adb logcat -d > "$RESULTS_DIR/android-logcat.txt"
  adb logcat -d --pid="$app_pid" > "$RESULTS_DIR/android-startup-app-logcat.txt"
  if grep -Eq "Can't add component because class|ArgumentNullException|NullReferenceException|MissingReferenceException|MissingComponentException|FATAL EXCEPTION" \
      "$RESULTS_DIR/android-startup-app-logcat.txt"; then
    echo "Android startup exception detected before the menu became ready" >&2
    exit 1
  fi
  if grep -Fq "BALLISTIC_ANDROID_MENU_READY version=3.3.0 screen=Menu" "$RESULTS_DIR/android-logcat.txt"; then
    menu_ready=1
    break
  fi
  sleep 1
done
test "$menu_ready" -eq 1
cp "$RESULTS_DIR/android-logcat.txt" "$RESULTS_DIR/android-menu-logcat.txt"

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
if grep -Eq "Can't add component because class|ArgumentNullException|NullReferenceException|MissingReferenceException|MissingComponentException|FATAL EXCEPTION" \
    "$RESULTS_DIR/android-app-logcat.txt"; then
  echo "Android runtime exception detected" >&2
  exit 1
fi

# Dial +1.0 MIL elevation on the static 200 m centre steel. This cancels
# its +0.15 MIL height plus the calculated 0.83 MIL drop, guaranteeing the
# bullseye path even at the maximum stage-one crosswind.
elevation_x=$((screen_width * 83 / 1000))
elevation_plus_y=$((screen_height * 625 / 1000))
adb shell input tap "$elevation_x" "$elevation_plus_y"
sleep 0.38
adb shell input tap "$elevation_x" "$elevation_plus_y"
sleep 0.38

fire_x=$((screen_width * 885 / 1000))
fire_y=$((screen_height * 872 / 1000))
adb logcat -c
adb shell input tap "$fire_x" "$fire_y"

fire_accepted=0
for _ in $(seq 1 80); do
  adb logcat -d > "$RESULTS_DIR/android-shot-logcat.txt"
  if grep -Fq "BALLISTIC_ANDROID_FIRE_ACCEPTED shot=1" "$RESULTS_DIR/android-shot-logcat.txt"; then
    fire_accepted=1
    break
  fi
  sleep 0.10
done
test "$fire_accepted" -eq 1

impact_closeup=0
for _ in $(seq 1 120); do
  adb logcat -d > "$RESULTS_DIR/android-shot-logcat.txt"
  if grep -Fq "BALLISTIC_ANDROID_IMPACT_CLOSEUP" "$RESULTS_DIR/android-shot-logcat.txt"; then
    impact_closeup=1
    adb exec-out screencap -p > "$RESULTS_DIR/android-impact-closeup.png"
    break
  fi
  sleep 0.05
done
test "$impact_closeup" -eq 1
grep -Eq "fov=17\.0 height=0\.[12][0-9] distance=2\.[56][0-9] viewport=0\.[45][0-9],0\.[45][0-9]" \
  "$RESULTS_DIR/android-shot-logcat.txt"

result_ready=0
for _ in $(seq 1 80); do
  adb logcat -d > "$RESULTS_DIR/android-shot-logcat.txt"
  if grep -Fq "BALLISTIC_ANDROID_RESULT_READY screen=Result gameplayVisible=False resultVisible=True acceptedShots=1" \
      "$RESULTS_DIR/android-shot-logcat.txt"; then
    result_ready=1
    break
  fi
  sleep 0.10
done
test "$result_ready" -eq 1
adb exec-out screencap -p > "$RESULTS_DIR/android-result-before-return.png"

# К ЦЕЛЯМ is now in the centre, outside FIRE's former bottom-right area.
# Clear the log first so any accidental fall-through shot is unambiguous.
result_x=$((screen_width * 500 / 1000))
result_y=$((screen_height * 885 / 1000))
adb logcat -c
adb shell input tap "$result_x" "$result_y"

returned=0
for _ in $(seq 1 60); do
  adb logcat -d > "$RESULTS_DIR/android-return-logcat.txt"
  if grep -Fq "BALLISTIC_ANDROID_RETURN_TO_TARGETS screen=Playing fireLocked=True gameplayVisible=True scopeVisible=True" \
      "$RESULTS_DIR/android-return-logcat.txt"; then
    returned=1
    break
  fi
  sleep 0.10
done
test "$returned" -eq 1
if grep -Fq "BALLISTIC_ANDROID_FIRE_ACCEPTED" "$RESULTS_DIR/android-return-logcat.txt"; then
  echo "К ЦЕЛЯМ fell through to FIRE" >&2
  exit 1
fi

# A real drag immediately after returning proves that aiming is available
# before the user deliberately presses FIRE again.
aim_x0=$((screen_width * 500 / 1000))
aim_y0=$((screen_height * 500 / 1000))
aim_x1=$((screen_width * 560 / 1000))
aim_y1=$((screen_height * 450 / 1000))
adb shell input swipe "$aim_x0" "$aim_y0" "$aim_x1" "$aim_y1" 260
sleep 0.65
adb logcat -d > "$RESULTS_DIR/android-return-logcat.txt"
grep -Fq "BALLISTIC_ANDROID_AIM_READY screen=Playing acceptedShots=1" "$RESULTS_DIR/android-return-logcat.txt"
if grep -Fq "BALLISTIC_ANDROID_FIRE_ACCEPTED" "$RESULTS_DIR/android-return-logcat.txt"; then
  echo "A shot fired before the player deliberately pressed FIRE" >&2
  exit 1
fi
adb exec-out screencap -p > "$RESULTS_DIR/android-aim-after-return.png"
adb logcat -d --pid="$app_pid" > "$RESULTS_DIR/android-complete-app-logcat.txt"
if grep -Eq "Can't add component because class|ArgumentNullException|NullReferenceException|MissingReferenceException|MissingComponentException|FATAL EXCEPTION" \
    "$RESULTS_DIR/android-complete-app-logcat.txt"; then
  echo "Android runtime exception detected during shot/review/return" >&2
  exit 1
fi

echo "Installed APK passed START, bullseye impact close-up, return-to-aim, and no-auto-fire tests at ${screen_width}x${screen_height}."
