from pathlib import Path

root = Path("LeiaWake")

# App identity and no Japanese model build.
p = root / "app/build.gradle.kts"
s = p.read_text()
s = s.replace('applicationId = "com.desmond.gptwake"', 'applicationId = "md.leia.parallel"')
s = s.replace('versionCode = 3', 'versionCode = 600')
s = s.replace('versionName = "1.1.0"', 'versionName = "6.0"')
old = '''androidComponents.onVariants { variant ->
    variant.sources.assets?.addGeneratedSourceDirectory(prepareJapaneseModels,
        PrepareJapaneseModels::outputDirectory)
}'''
s = s.replace(old, '')
p.write_text(s)

# Wake phrase.
p = root / "app/src/main/java/com/desmond/gptwake/WakeWordStore.java"
s = p.read_text()
s = s.replace('public static final String DEFAULT_PHRASE = "芝麻开门";',
              'public static final String DEFAULT_PHRASE = "Ок Лея";')
s = s.replace('public static final String DEFAULT_LINE = "zh ī m á k āi m én @芝麻开门";',
              'public static final String DEFAULT_LINE = "OW1 K L EH1 Y AH0 @Ок_Лея";')
p.write_text(s)

p = root / "app/src/main/java/com/desmond/gptwake/KwsEngine.java"
s = p.read_text().replace('public static final float DEFAULT_THRESHOLD = 0.40f;',
                          'public static final float DEFAULT_THRESHOLD = 0.32f;')
p.write_text(s)

# Google remains assistant; don't gate the app on ChatGPT's assistant role.
p = root / "app/src/main/java/com/desmond/gptwake/ui/WakeUiState.kt"
s = p.read_text()
old = '''    // ChatGPT must hold the assistant role or it cannot record under keyguard. This app must never
    // take that role for itself.
    assistant = Settings.Secure.getString(context.contentResolver, "voice_interaction_service")
        ?.let(android.content.ComponentName::unflattenFromString)
        ?.packageName == "com.openai.chatgpt",
'''
new = '''    // Google/Gemini stays the system assistant so "Hey Google" keeps working.
    // Ok Leia waits for user unlock before starting ChatGPT Voice.
    assistant = true,
'''
if old not in s:
    raise SystemExit("assistant block not found")
s = s.replace(old, new)
p.write_text(s)

# WakeController waits for unlock, then launches ChatGPT and arms Voice clicker.
p = root / "app/src/main/java/com/desmond/gptwake/WakeController.java"
s = p.read_text()
s = s.replace('import android.content.Context;\n',
              'import android.app.KeyguardManager;\nimport android.content.Context;\nimport android.content.Intent;\n')
old = '''    private void launchChatGpt() {
        if (state != State.MIC_HANDOFF) return;
        if (hasCommunication()) {
            pauseForCommunication();
            return;
        }
        set(State.CHATGPT_LAUNCHING);
        launchStartedAt = SystemClock.elapsedRealtime();
        deeplinkTried = false;
        L.i("CHATGPT_LAUNCH_ATTEMPT route=AssistantActivity");
        GptLauncher.launchDirect(ctx);
        later(this::checkVoiceConfirm, VOICE_CONFIRM_MS);
    }
'''
new = '''    private void launchChatGpt() {
        if (state != State.MIC_HANDOFF) return;
        if (hasCommunication()) {
            pauseForCommunication();
            return;
        }
        set(State.CHATGPT_LAUNCHING);
        launchStartedAt = SystemClock.elapsedRealtime();
        deeplinkTried = false;

        KeyguardManager km = ctx.getSystemService(KeyguardManager.class);
        if (km != null && km.isDeviceLocked()) {
            L.i("WAITING_FOR_USER_UNLOCK");
            try {
                ctx.startActivity(new Intent(ctx, UnlockRelayActivity.class)
                        .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK
                                | Intent.FLAG_ACTIVITY_CLEAR_TOP
                                | Intent.FLAG_ACTIVITY_SINGLE_TOP));
            } catch (Throwable t) {
                L.e("UNLOCK_RELAY_LAUNCH_FAIL", t);
                reacquire();
            }
            return;
        }
        continueAfterUnlock();
    }

    public void continueAfterUnlock() {
        post(() -> {
            if (state != State.CHATGPT_LAUNCHING) return;
            launchStartedAt = SystemClock.elapsedRealtime();
            deeplinkTried = false;
            VoiceClickService.arm(ctx);
            L.i("CHATGPT_LAUNCH_AFTER_UNLOCK route=AssistantActivity");
            boolean direct = GptLauncher.launchDirect(ctx);
            if (!direct) {
                L.i("CHATGPT_DIRECT_UNAVAILABLE falling back immediately");
                deeplinkTried = true;
                GptLauncher.launchDeeplink(ctx);
                later(this::checkVoiceConfirm, DEEPLINK_CONFIRM_MS);
            } else {
                later(this::checkVoiceConfirm, VOICE_CONFIRM_MS);
            }
        });
    }
'''
if old not in s:
    raise SystemExit("launch block not found")
s = s.replace(old, new)
p.write_text(s)

# Copy new source/resources.
src = Path("leia_v6_patch")
(root / "app/src/main/java/com/desmond/gptwake/UnlockRelayActivity.java").write_text(
    (src / "UnlockRelayActivity.java").read_text())
(root / "app/src/main/java/com/desmond/gptwake/VoiceClickService.java").write_text(
    (src / "VoiceClickService.java").read_text())
(root / "app/src/main/res/xml/voice_click_service.xml").write_text(
    (src / "voice_click_service.xml").read_text())

# Manifest: remove notification listener and add relay/accessibility service.
p = root / "app/src/main/AndroidManifest.xml"
s = p.read_text()
start = s.find('        <!-- Optional: gives the screen-on path a Hang Up action.')
if start != -1:
    end = s.find('        </service>\n', start)
    if end != -1:
        end += len('        </service>\n')
        s = s[:start] + s[end:]

insert = '''
        <activity
            android:name=".UnlockRelayActivity"
            android:exported="false"
            android:showWhenLocked="true"
            android:turnScreenOn="true"
            android:excludeFromRecents="true"
            android:noHistory="true"
            android:theme="@style/Theme.GPTWake" />

        <service
            android:name=".VoiceClickService"
            android:label="Ок Лея — запуск Voice в ChatGPT"
            android:permission="android.permission.BIND_ACCESSIBILITY_SERVICE"
            android:exported="true">
            <intent-filter>
                <action android:name="android.accessibilityservice.AccessibilityService" />
            </intent-filter>
            <meta-data
                android:name="android.accessibilityservice"
                android:resource="@xml/voice_click_service" />
        </service>
'''
marker = '        <receiver\n            android:name=".BootReceiver"'
if marker not in s:
    raise SystemExit("manifest marker not found")
s = s.replace(marker, insert + '\n' + marker)
p.write_text(s)

# UI strings.
p = root / "app/src/main/res/values/strings.xml"
s = p.read_text()
repl = {
    '<string name="app_name">GPTWake</string>':
        '<string name="app_name">Ок Лея</string>',
    '<string name="tagline">Offline wake word · opens ChatGPT voice</string>':
        '<string name="tagline">«Ок, Лея» параллельно с «Окей Google»</string>',
    '<string name="notification_listening">GPTWake is listening for the wake word</string>':
        '<string name="notification_listening">Ок Лея слушает ключевую фразу</string>',
    '<string name="privacy_note">Wake word detection runs entirely on this device; no audio is uploaded</string>':
        '<string name="privacy_note">«Ок, Лея» распознаётся локально. Google/Gemini остаётся системным помощником.</string>',
    '<string name="perm_assistant">ChatGPT is the system default assistant</string>':
        '<string name="perm_assistant">Google/Gemini остаётся системным помощником</string>',
    '<string name="perm_assistant_why">Otherwise ChatGPT can\\\'t get the microphone on the lock screen</string>':
        '<string name="perm_assistant_why">Не меняй системного помощника — «Окей Google» должен продолжать работать</string>',
    '<string name="channel_listening">Wake word listening</string>':
        '<string name="channel_listening">Ок Лея — фоновое прослушивание</string>',
}
for a,b in repl.items():
    s = s.replace(a,b)
s = s.replace('</resources>',
              '    <string name="voice_click_description">После разблокировки открывает ChatGPT и нажимает кнопку Voice. Работает только с приложением ChatGPT.</string>\n</resources>')
p.write_text(s)

# Editor dictionary support.
p = root / "app/src/main/assets/kws/en.phone"
with p.open("a", encoding="utf-8") as f:
    f.write("\nLEIA L EH1 Y AH0\n")
