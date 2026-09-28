package md.leia.launcher;

import android.app.*;
import android.content.*;
import android.graphics.PixelFormat;
import android.net.Uri;
import android.os.*;
import android.provider.Settings;
import android.speech.RecognitionListener;
import android.speech.RecognizerIntent;
import android.speech.SpeechRecognizer;
import android.view.Gravity;
import android.view.WindowManager;
import android.widget.TextView;

import java.util.ArrayList;
import java.util.Locale;

public class WakeService extends Service implements RecognitionListener {
    public static final String ACTION_RESUME = "md.leia.launcher.RESUME";
    public static final String ACTION_TEST_VOICE = "md.leia.launcher.TEST_VOICE";

    private static final String CHANNEL_ID = "leia_listening";
    private SpeechRecognizer recognizer;
    private Intent recognizerIntent;
    private final Handler handler = new Handler(Looper.getMainLooper());

    private boolean listeningEnabled = true;
    private boolean destroyed = false;
    private boolean launching = false;
    private long lastLaunchAt = 0L;

    private WindowManager windowManager;
    private TextView bubble;

    @Override public void onCreate() {
        super.onCreate();
        createChannel();
        startForeground(7, buildNotification("Лея готова. Скажи «Ок, Лея»"));
        showOverlayBubble();
        setupRecognizer();
        handler.postDelayed(this::listen, 500);
    }

    @Override public int onStartCommand(Intent intent, int flags, int startId) {
        String action = intent != null ? intent.getAction() : null;

        if (ACTION_TEST_VOICE.equals(action)) {
            launchChatGptVoice();
        } else if (ACTION_RESUME.equals(action)) {
            resumeListening();
        }

        return START_STICKY;
    }

    private void setupRecognizer() {
        if (recognizer != null || destroyed) return;
        try {
            recognizer = SpeechRecognizer.createSpeechRecognizer(this);
            recognizer.setRecognitionListener(this);

            recognizerIntent = new Intent(RecognizerIntent.ACTION_RECOGNIZE_SPEECH);
            recognizerIntent.putExtra(RecognizerIntent.EXTRA_LANGUAGE_MODEL, RecognizerIntent.LANGUAGE_MODEL_FREE_FORM);
            recognizerIntent.putExtra(RecognizerIntent.EXTRA_LANGUAGE, "ru-RU");
            recognizerIntent.putExtra(RecognizerIntent.EXTRA_LANGUAGE_PREFERENCE, "ru-RU");
            recognizerIntent.putExtra(RecognizerIntent.EXTRA_PARTIAL_RESULTS, true);
            recognizerIntent.putExtra(RecognizerIntent.EXTRA_MAX_RESULTS, 7);
            recognizerIntent.putExtra(RecognizerIntent.EXTRA_PREFER_OFFLINE, true);
        } catch (Throwable t) {
            updateNotification("Ошибка распознавания речи. Открой приложение.");
        }
    }

    private void listen() {
        if (!listeningEnabled || destroyed || launching) return;
        if (recognizer == null) setupRecognizer();
        if (recognizer == null) return;

        try {
            recognizer.startListening(recognizerIntent);
            updateNotification("Слушаю: «Ок, Лея»");
        } catch (Throwable t) {
            restartSoon(1200);
        }
    }

    private void resumeListening() {
        launching = false;
        listeningEnabled = true;
        if (recognizer == null) setupRecognizer();
        updateNotification("Лея снова слушает «Ок, Лея»");
        handler.removeCallbacksAndMessages(null);
        handler.postDelayed(this::listen, 500);
    }

    private boolean isWakePhrase(String input) {
        if (input == null) return false;

        String s = input.toLowerCase(Locale.ROOT)
                .replace('ё','е')
                .replaceAll("[^a-zа-я0-9 ]", " ")
                .replaceAll("\\s+", " ")
                .trim();

        boolean ok =
                s.contains("окей") ||
                s.contains("okay") ||
                s.startsWith("ок ") ||
                s.equals("ок") ||
                s.contains(" ок ");

        boolean leia =
                s.contains("лея") ||
                s.contains("леа") ||
                s.contains("леиа") ||
                s.contains("лейя") ||
                s.contains("лия") ||
                s.contains("leia") ||
                s.contains("leya");

        return ok && leia;
    }

    private void inspect(ArrayList<String> matches) {
        if (matches == null || launching) return;

        for (String s : matches) {
            if (isWakePhrase(s)) {
                launchChatGptVoice();
                return;
            }
        }
    }

    private void launchChatGptVoice() {
        long now = System.currentTimeMillis();
        if (launching || now - lastLaunchAt < 3000) return;

        launching = true;
        lastLaunchAt = now;
        listeningEnabled = false;

        releaseRecognizer();
        updateNotification("«Ок, Лея» услышано — запускаю ChatGPT Voice");

        handler.postDelayed(() -> {
            try {
                Intent voice = new Intent(Intent.ACTION_VIEW, Uri.parse("https://chatgpt.com/"));
                voice.setPackage("com.openai.chatgpt");
                voice.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK | Intent.FLAG_ACTIVITY_CLEAR_TOP);
                startActivity(voice);
            } catch (Throwable first) {
                try {
                    Intent launch = getPackageManager().getLaunchIntentForPackage("com.openai.chatgpt");
                    if (launch != null) {
                        launch.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK | Intent.FLAG_ACTIVITY_CLEAR_TOP);
                        startActivity(launch);
                    } else {
                        Intent browser = new Intent(Intent.ACTION_VIEW, Uri.parse("https://chatgpt.com/"));
                        browser.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
                        startActivity(browser);
                    }
                } catch (Throwable ignored) {
                    updateNotification("Не удалось открыть ChatGPT");
                    launching = false;
                }
            }

            updateNotification("ChatGPT открыт. Для следующего вызова нажми «Возобновить».");
        }, 450);
    }

    private void releaseRecognizer() {
        try {
            if (recognizer != null) {
                recognizer.cancel();
                recognizer.destroy();
            }
        } catch (Throwable ignored) {}
        recognizer = null;
    }

    private void restartSoon(long delay) {
        if (!listeningEnabled || destroyed || launching) return;
        handler.postDelayed(this::listen, delay);
    }

    private Notification buildNotification(String text) {
        Intent open = new Intent(this, MainActivity.class);
        PendingIntent openPi = PendingIntent.getActivity(
                this, 1, open, PendingIntent.FLAG_IMMUTABLE | PendingIntent.FLAG_UPDATE_CURRENT);

        Intent resume = new Intent(this, WakeService.class);
        resume.setAction(ACTION_RESUME);
        PendingIntent resumePi = PendingIntent.getService(
                this, 2, resume, PendingIntent.FLAG_IMMUTABLE | PendingIntent.FLAG_UPDATE_CURRENT);

        Notification.Action resumeAction = new Notification.Action.Builder(
                android.R.drawable.ic_btn_speak_now, "Возобновить", resumePi).build();

        return new Notification.Builder(this, CHANNEL_ID)
                .setContentTitle("Ок, Лея")
                .setContentText(text)
                .setSmallIcon(android.R.drawable.ic_btn_speak_now)
                .setOngoing(true)
                .setContentIntent(openPi)
                .addAction(resumeAction)
                .build();
    }

    private void updateNotification(String text) {
        try {
            NotificationManager nm = getSystemService(NotificationManager.class);
            nm.notify(7, buildNotification(text));
        } catch (Throwable ignored) {}
    }

    private void createChannel() {
        NotificationManager nm = getSystemService(NotificationManager.class);
        if (Build.VERSION.SDK_INT >= 26) {
            NotificationChannel ch = new NotificationChannel(
                    CHANNEL_ID,
                    "Прослушивание Ок Лея",
                    NotificationManager.IMPORTANCE_LOW);
            ch.setDescription("Фоновое распознавание фразы «Ок, Лея»");
            nm.createNotificationChannel(ch);
        }
    }

    private void showOverlayBubble() {
        if (!Settings.canDrawOverlays(this)) return;

        try {
            windowManager = (WindowManager) getSystemService(WINDOW_SERVICE);
            bubble = new TextView(this);
            bubble.setText("Л");
            bubble.setTextSize(12);
            bubble.setGravity(Gravity.CENTER);
            bubble.setTextColor(0xFFFFFFFF);
            bubble.setBackgroundColor(0x88444444);

            int size = (int)(28 * getResources().getDisplayMetrics().density);
            WindowManager.LayoutParams lp = new WindowManager.LayoutParams(
                    size,
                    size,
                    Build.VERSION.SDK_INT >= 26
                            ? WindowManager.LayoutParams.TYPE_APPLICATION_OVERLAY
                            : WindowManager.LayoutParams.TYPE_PHONE,
                    WindowManager.LayoutParams.FLAG_NOT_FOCUSABLE |
                            WindowManager.LayoutParams.FLAG_NOT_TOUCH_MODAL,
                    PixelFormat.TRANSLUCENT);

            lp.gravity = Gravity.TOP | Gravity.END;
            lp.x = 6;
            lp.y = (int)(90 * getResources().getDisplayMetrics().density);
            windowManager.addView(bubble, lp);
        } catch (Throwable ignored) {}
    }

    private void hideOverlayBubble() {
        try {
            if (windowManager != null && bubble != null) windowManager.removeView(bubble);
        } catch (Throwable ignored) {}
        bubble = null;
    }

    @Override public void onDestroy() {
        destroyed = true;
        listeningEnabled = false;
        handler.removeCallbacksAndMessages(null);
        releaseRecognizer();
        hideOverlayBubble();
        super.onDestroy();
    }

    @Override public android.os.IBinder onBind(Intent intent) { return null; }

    @Override public void onReadyForSpeech(Bundle params) {}
    @Override public void onBeginningOfSpeech() {}
    @Override public void onRmsChanged(float rmsdB) {}
    @Override public void onBufferReceived(byte[] buffer) {}
    @Override public void onEndOfSpeech() {}

    @Override public void onError(int error) {
        if (!listeningEnabled || launching) return;

        long delay;
        if (error == SpeechRecognizer.ERROR_RECOGNIZER_BUSY) delay = 1400;
        else if (error == SpeechRecognizer.ERROR_NETWORK || error == SpeechRecognizer.ERROR_NETWORK_TIMEOUT) delay = 1800;
        else delay = 550;

        restartSoon(delay);
    }

    @Override public void onResults(Bundle results) {
        inspect(results.getStringArrayList(SpeechRecognizer.RESULTS_RECOGNITION));
        restartSoon(450);
    }

    @Override public void onPartialResults(Bundle partialResults) {
        inspect(partialResults.getStringArrayList(SpeechRecognizer.RESULTS_RECOGNITION));
    }

    @Override public void onEvent(int eventType, Bundle params) {}
}
