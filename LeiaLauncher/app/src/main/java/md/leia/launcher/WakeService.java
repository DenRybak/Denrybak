package md.leia.launcher;

import android.app.*;
import android.content.*;
import android.graphics.PixelFormat;
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
    private static final String CHANNEL_ID = "leia_listening";
    private SpeechRecognizer recognizer;
    private Intent recognizerIntent;
    private final Handler handler = new Handler(Looper.getMainLooper());
    private boolean stopping = false;
    private WindowManager windowManager;
    private TextView bubble;

    @Override public void onCreate() {
        super.onCreate();
        createChannel();
        startForeground(7, buildNotification("Лея слушает фразу «Ок, Лея»"));
        showOverlayBubble();
        setupRecognizer();
        handler.postDelayed(this::listen, 400);
    }

    @Override public int onStartCommand(Intent intent, int flags, int startId) {
        return START_STICKY;
    }

    private void setupRecognizer() {
        try {
            if (Build.VERSION.SDK_INT >= 31 && SpeechRecognizer.isOnDeviceRecognitionAvailable(this)) {
                recognizer = SpeechRecognizer.createOnDeviceSpeechRecognizer(this);
            } else {
                recognizer = SpeechRecognizer.createSpeechRecognizer(this);
            }
        } catch (Throwable t) {
            recognizer = SpeechRecognizer.createSpeechRecognizer(this);
        }
        recognizer.setRecognitionListener(this);
        recognizerIntent = new Intent(RecognizerIntent.ACTION_RECOGNIZE_SPEECH);
        recognizerIntent.putExtra(RecognizerIntent.EXTRA_LANGUAGE_MODEL, RecognizerIntent.LANGUAGE_MODEL_FREE_FORM);
        recognizerIntent.putExtra(RecognizerIntent.EXTRA_LANGUAGE, "ru-RU");
        recognizerIntent.putExtra(RecognizerIntent.EXTRA_PARTIAL_RESULTS, true);
        recognizerIntent.putExtra(RecognizerIntent.EXTRA_MAX_RESULTS, 5);
    }

    private void listen() {
        if (stopping || recognizer == null) return;
        try { recognizer.startListening(recognizerIntent); }
        catch (Throwable ignored) { restartSoon(900); }
    }

    private boolean isWakePhrase(String input) {
        if (input == null) return false;
        String s = input.toLowerCase(Locale.ROOT)
                .replace('ё','е')
                .replaceAll("[^a-zа-я0-9 ]", " ")
                .replaceAll("\\s+", " ")
                .trim();
        boolean ok = s.contains("ок ") || s.startsWith("ок") || s.contains("окей") || s.contains("okay");
        boolean leia = s.contains("лея") || s.contains("леа") || s.contains("лия") || s.contains("леиа") || s.contains("leia") || s.contains("leya");
        return ok && leia;
    }

    private void inspect(ArrayList<String> matches) {
        if (matches == null) return;
        for (String s : matches) {
            if (isWakePhrase(s)) {
                openChatGpt();
                return;
            }
        }
    }

    private void openChatGpt() {
        try {
            Intent launch = getPackageManager().getLaunchIntentForPackage("com.openai.chatgpt");
            if (launch == null) {
                launch = new Intent(Intent.ACTION_VIEW, android.net.Uri.parse("https://chatgpt.com/"));
            }
            launch.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK | Intent.FLAG_ACTIVITY_CLEAR_TOP);
            startActivity(launch);
            updateNotification("Фраза услышана — открываю ChatGPT");
        } catch (Throwable t) {
            updateNotification("Не удалось открыть ChatGPT — нажми уведомление");
        }
        restartSoon(1800);
    }

    private void restartSoon(long delay) {
        if (!stopping) handler.postDelayed(this::listen, delay);
    }

    private Notification buildNotification(String text) {
        Intent open = new Intent(this, MainActivity.class);
        PendingIntent pi = PendingIntent.getActivity(this, 1, open, PendingIntent.FLAG_IMMUTABLE | PendingIntent.FLAG_UPDATE_CURRENT);
        return new Notification.Builder(this, CHANNEL_ID)
                .setContentTitle("Ок, Лея")
                .setContentText(text)
                .setSmallIcon(android.R.drawable.ic_btn_speak_now)
                .setOngoing(true)
                .setContentIntent(pi)
                .build();
    }

    private void updateNotification(String text) {
        NotificationManager nm = getSystemService(NotificationManager.class);
        nm.notify(7, buildNotification(text));
    }

    private void createChannel() {
        NotificationManager nm = getSystemService(NotificationManager.class);
        if (Build.VERSION.SDK_INT >= 26) {
            NotificationChannel ch = new NotificationChannel(CHANNEL_ID, "Прослушивание Ок Лея", NotificationManager.IMPORTANCE_LOW);
            ch.setDescription("Постоянное уведомление необходимо Android для фонового доступа к микрофону");
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
                    size, size,
                    Build.VERSION.SDK_INT >= 26 ? WindowManager.LayoutParams.TYPE_APPLICATION_OVERLAY : WindowManager.LayoutParams.TYPE_PHONE,
                    WindowManager.LayoutParams.FLAG_NOT_FOCUSABLE | WindowManager.LayoutParams.FLAG_NOT_TOUCH_MODAL,
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
        stopping = true;
        handler.removeCallbacksAndMessages(null);
        try { if (recognizer != null) recognizer.destroy(); } catch (Throwable ignored) {}
        hideOverlayBubble();
        super.onDestroy();
    }

    @Override public android.os.IBinder onBind(Intent intent) { return null; }
    @Override public void onReadyForSpeech(Bundle params) {}
    @Override public void onBeginningOfSpeech() {}
    @Override public void onRmsChanged(float rmsdB) {}
    @Override public void onBufferReceived(byte[] buffer) {}
    @Override public void onEndOfSpeech() { restartSoon(350); }
    @Override public void onError(int error) { restartSoon(error == SpeechRecognizer.ERROR_RECOGNIZER_BUSY ? 1200 : 500); }
    @Override public void onResults(Bundle results) {
        inspect(results.getStringArrayList(SpeechRecognizer.RESULTS_RECOGNITION));
        restartSoon(350);
    }
    @Override public void onPartialResults(Bundle partialResults) {
        inspect(partialResults.getStringArrayList(SpeechRecognizer.RESULTS_RECOGNITION));
    }
    @Override public void onEvent(int eventType, Bundle params) {}
}
