package md.leia.assistant;

import android.content.BroadcastReceiver;
import android.content.ComponentName;
import android.content.Context;
import android.content.Intent;
import android.content.IntentFilter;
import android.os.Build;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.service.voice.VoiceInteractionService;
import android.speech.RecognitionListener;
import android.speech.RecognizerIntent;
import android.speech.SpeechRecognizer;

import java.util.ArrayList;
import java.util.Locale;

public class LeiaVoiceInteractionService extends VoiceInteractionService implements RecognitionListener {
    public static final String ACTION_RESUME_WAKE = "md.leia.assistant.RESUME_WAKE";
    public static final String PREFS = "leia_settings";
    public static final String KEY_WAKE_ENABLED = "wake_enabled";

    private final Handler handler = new Handler(Looper.getMainLooper());
    private SpeechRecognizer recognizer;
    private Intent recognizerIntent;
    private boolean wakePaused;
    private boolean destroyed;
    private BroadcastReceiver receiver;

    @Override
    public void onReady() {
        super.onReady();

        receiver = new BroadcastReceiver() {
            @Override public void onReceive(Context context, Intent intent) {
                if (ACTION_RESUME_WAKE.equals(intent.getAction())) {
                    wakePaused = false;
                    handler.postDelayed(() -> startWakeListening(), 500);
                }
            }
        };
        IntentFilter f = new IntentFilter(ACTION_RESUME_WAKE);
        if (Build.VERSION.SDK_INT >= 33) {
            registerReceiver(receiver, f, Context.RECEIVER_NOT_EXPORTED);
        } else {
            registerReceiver(receiver, f);
        }

        handler.postDelayed(this::startWakeListening, 1000);
    }

    @Override
    public void onLaunchVoiceAssistFromKeyguard() {
        showSession(new Bundle(), 0);
    }

    private boolean wakeEnabled() {
        return getSharedPreferences(PREFS, MODE_PRIVATE).getBoolean(KEY_WAKE_ENABLED, true);
    }

    private void ensureRecognizer() {
        if (recognizer != null) return;
        ComponentName external = SpeechUtils.findExternalRecognizer(this);
        recognizer = external != null
                ? SpeechRecognizer.createSpeechRecognizer(this, external)
                : SpeechRecognizer.createSpeechRecognizer(this);
        recognizer.setRecognitionListener(this);

        recognizerIntent = new Intent(RecognizerIntent.ACTION_RECOGNIZE_SPEECH);
        recognizerIntent.putExtra(RecognizerIntent.EXTRA_LANGUAGE_MODEL, RecognizerIntent.LANGUAGE_MODEL_FREE_FORM);
        recognizerIntent.putExtra(RecognizerIntent.EXTRA_LANGUAGE, "ru-RU");
        recognizerIntent.putExtra(RecognizerIntent.EXTRA_LANGUAGE_PREFERENCE, "ru-RU");
        recognizerIntent.putExtra(RecognizerIntent.EXTRA_PARTIAL_RESULTS, true);
        recognizerIntent.putExtra(RecognizerIntent.EXTRA_MAX_RESULTS, 5);
    }

    private void startWakeListening() {
        if (destroyed || wakePaused || !wakeEnabled()) return;
        try {
            ensureRecognizer();
            recognizer.startListening(recognizerIntent);
        } catch (Throwable t) {
            handler.postDelayed(this::startWakeListening, 1500);
        }
    }

    private boolean isWakePhrase(String s) {
        if (s == null) return false;
        String n = s.toLowerCase(Locale.ROOT)
                .replace('ё', 'е')
                .replaceAll("[^a-zа-я0-9 ]", " ")
                .replaceAll("\\s+", " ")
                .trim();
        boolean ok = n.contains("ок лея") || n.contains("окей лея") ||
                n.contains("ок леа") || n.contains("окей леа") ||
                n.contains("okay leia") || n.contains("ok leia");
        return ok;
    }

    private void inspect(ArrayList<String> matches) {
        if (matches == null || wakePaused) return;
        for (String s : matches) {
            if (isWakePhrase(s)) {
                wakePaused = true;
                try { recognizer.cancel(); } catch (Throwable ignored) {}
                showSession(new Bundle(), 0);
                return;
            }
        }
    }

    private void restartSoon(long delay) {
        if (!wakePaused && !destroyed && wakeEnabled()) {
            handler.postDelayed(this::startWakeListening, delay);
        }
    }

    @Override public void onReadyForSpeech(Bundle params) {}
    @Override public void onBeginningOfSpeech() {}
    @Override public void onRmsChanged(float rmsdB) {}
    @Override public void onBufferReceived(byte[] buffer) {}
    @Override public void onEndOfSpeech() {}
    @Override public void onError(int error) { restartSoon(error == SpeechRecognizer.ERROR_RECOGNIZER_BUSY ? 1200 : 450); }
    @Override public void onResults(Bundle results) {
        inspect(results.getStringArrayList(SpeechRecognizer.RESULTS_RECOGNITION));
        restartSoon(350);
    }
    @Override public void onPartialResults(Bundle partialResults) {
        inspect(partialResults.getStringArrayList(SpeechRecognizer.RESULTS_RECOGNITION));
    }
    @Override public void onEvent(int eventType, Bundle params) {}

    @Override
    public void onShutdown() {
        destroyed = true;
        handler.removeCallbacksAndMessages(null);
        try { if (recognizer != null) recognizer.destroy(); } catch (Throwable ignored) {}
        recognizer = null;
        if (receiver != null) {
            try { unregisterReceiver(receiver); } catch (Throwable ignored) {}
        }
        super.onShutdown();
    }
}
