package md.leia.assistant;

import android.content.ComponentName;
import android.content.Context;
import android.content.Intent;
import android.graphics.Color;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.service.voice.VoiceInteractionSession;
import android.speech.RecognitionListener;
import android.speech.RecognizerIntent;
import android.speech.SpeechRecognizer;
import android.speech.tts.TextToSpeech;
import android.speech.tts.UtteranceProgressListener;
import android.view.Gravity;
import android.view.View;
import android.view.ViewGroup;
import android.widget.LinearLayout;
import android.widget.TextView;

import java.util.ArrayList;
import java.util.Locale;

public class LeiaSession extends VoiceInteractionSession implements RecognitionListener {
    private final Context context;
    private final Handler handler = new Handler(Looper.getMainLooper());

    private SpeechRecognizer recognizer;
    private Intent recognizerIntent;
    private TextToSpeech tts;

    private TextView state;
    private TextView transcript;
    private TextView answer;

    private boolean destroyed;
    private boolean busy;
    private boolean ttsReady;

    public LeiaSession(Context context) {
        super(context);
        this.context = context;
    }

    @Override
    public void onCreate() {
        super.onCreate();
        initSpeech();
        initTts();
    }

    @Override
    public View onCreateContentView() {
        int p = (int)(18 * context.getResources().getDisplayMetrics().density);

        LinearLayout root = new LinearLayout(context);
        root.setOrientation(LinearLayout.VERTICAL);
        root.setPadding(p, p, p, p);
        root.setBackgroundColor(0xF5FFFFFF);

        TextView title = new TextView(context);
        title.setText("Лея");
        title.setTextSize(28);
        title.setTextColor(Color.BLACK);
        title.setGravity(Gravity.CENTER);
        root.addView(title, full());

        state = new TextView(context);
        state.setText("Готова");
        state.setTextSize(16);
        state.setTextColor(0xFF555555);
        state.setGravity(Gravity.CENTER);
        state.setPadding(0, 4, 0, p / 2);
        root.addView(state, full());

        transcript = new TextView(context);
        transcript.setTextSize(18);
        transcript.setTextColor(Color.DKGRAY);
        transcript.setPadding(0, p / 2, 0, p / 2);
        root.addView(transcript, full());

        answer = new TextView(context);
        answer.setTextSize(19);
        answer.setTextColor(Color.BLACK);
        answer.setPadding(0, p / 2, 0, p);
        root.addView(answer, full());

        TextView hint = new TextView(context);
        hint.setText("Скажи команду или задай вопрос.");
        hint.setTextSize(14);
        hint.setTextColor(Color.GRAY);
        hint.setGravity(Gravity.CENTER);
        root.addView(hint, full());

        root.setOnClickListener(v -> {
            if (!busy) startListening();
        });

        return root;
    }

    private LinearLayout.LayoutParams full() {
        return new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT,
                ViewGroup.LayoutParams.WRAP_CONTENT);
    }

    @Override
    public void onShow(Bundle args, int showFlags) {
        super.onShow(args, showFlags);
        handler.postDelayed(this::startListening, 300);
    }

    private void initSpeech() {
        try {
            ComponentName external = SpeechUtils.findExternalRecognizer(context);
            recognizer = external != null
                    ? SpeechRecognizer.createSpeechRecognizer(context, external)
                    : SpeechRecognizer.createSpeechRecognizer(context);
            recognizer.setRecognitionListener(this);

            recognizerIntent = new Intent(RecognizerIntent.ACTION_RECOGNIZE_SPEECH);
            recognizerIntent.putExtra(RecognizerIntent.EXTRA_LANGUAGE_MODEL, RecognizerIntent.LANGUAGE_MODEL_FREE_FORM);
            recognizerIntent.putExtra(RecognizerIntent.EXTRA_LANGUAGE, "ru-RU");
            recognizerIntent.putExtra(RecognizerIntent.EXTRA_LANGUAGE_PREFERENCE, "ru-RU");
            recognizerIntent.putExtra(RecognizerIntent.EXTRA_PARTIAL_RESULTS, true);
            recognizerIntent.putExtra(RecognizerIntent.EXTRA_MAX_RESULTS, 5);
        } catch (Throwable ignored) {}
    }

    private void initTts() {
        tts = new TextToSpeech(context, status -> {
            if (status == TextToSpeech.SUCCESS) {
                ttsReady = true;
                tts.setLanguage(new Locale("ru", "RU"));
                tts.setSpeechRate(1.02f);
                tts.setOnUtteranceProgressListener(new UtteranceProgressListener() {
                    @Override public void onStart(String utteranceId) {}
                    @Override public void onDone(String utteranceId) {
                        handler.postDelayed(() -> {
                            busy = false;
                            if (state != null) state.setText("Слушаю");
                            startListening();
                        }, 350);
                    }
                    @Override public void onError(String utteranceId) {
                        handler.post(() -> busy = false);
                    }
                });
            }
        });
    }

    private void startListening() {
        if (destroyed || busy || recognizer == null) return;
        try {
            if (state != null) state.setText("Слушаю…");
            recognizer.startListening(recognizerIntent);
        } catch (Throwable t) {
            if (state != null) state.setText("Нажми на окно и повтори");
        }
    }

    private void handleText(String text) {
        if (text == null || text.isBlank()) return;
        busy = true;
        try { recognizer.cancel(); } catch (Throwable ignored) {}

        if (transcript != null) transcript.setText("Вы: " + text);
        if (state != null) state.setText("Думаю…");
        if (answer != null) answer.setText("");

        CommandRouter.execute(context, text, reply -> handler.post(() -> {
            if (destroyed) return;
            if (answer != null) answer.setText("Лея: " + reply);
            if (state != null) state.setText("Отвечаю");
            speak(reply);
        }));
    }

    private void speak(String text) {
        if (!ttsReady || tts == null) {
            busy = false;
            if (state != null) state.setText("Готова");
            return;
        }
        Bundle b = new Bundle();
        tts.speak(text, TextToSpeech.QUEUE_FLUSH, b, "leia_reply");
    }

    @Override public void onReadyForSpeech(Bundle params) {
        if (state != null) state.setText("Говори");
    }
    @Override public void onBeginningOfSpeech() {}
    @Override public void onRmsChanged(float rmsdB) {}
    @Override public void onBufferReceived(byte[] buffer) {}
    @Override public void onEndOfSpeech() {
        if (state != null && !busy) state.setText("Распознаю…");
    }
    @Override public void onError(int error) {
        if (!busy && !destroyed) handler.postDelayed(this::startListening, 600);
    }
    @Override public void onResults(Bundle results) {
        ArrayList<String> list = results.getStringArrayList(SpeechRecognizer.RESULTS_RECOGNITION);
        if (list != null && !list.isEmpty()) handleText(list.get(0));
        else if (!busy) handler.postDelayed(this::startListening, 400);
    }
    @Override public void onPartialResults(Bundle partialResults) {
        ArrayList<String> list = partialResults.getStringArrayList(SpeechRecognizer.RESULTS_RECOGNITION);
        if (list != null && !list.isEmpty() && transcript != null && !busy) {
            transcript.setText("Вы: " + list.get(0));
        }
    }
    @Override public void onEvent(int eventType, Bundle params) {}

    @Override
    public void onDestroy() {
        destroyed = true;
        handler.removeCallbacksAndMessages(null);
        try { if (recognizer != null) recognizer.destroy(); } catch (Throwable ignored) {}
        try { if (tts != null) { tts.stop(); tts.shutdown(); } } catch (Throwable ignored) {}
        context.sendBroadcast(new Intent(LeiaVoiceInteractionService.ACTION_RESUME_WAKE)
                .setPackage(context.getPackageName()));
        super.onDestroy();
    }
}
