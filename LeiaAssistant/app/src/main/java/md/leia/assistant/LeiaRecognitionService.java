package md.leia.assistant;

import android.content.Intent;
import android.os.Bundle;
import android.speech.RecognitionService;
import android.speech.SpeechRecognizer;

public class LeiaRecognitionService extends RecognitionService {
    @Override
    protected void onStartListening(Intent recognizerIntent, Callback listener) {
        Bundle b = new Bundle();
        listener.error(SpeechRecognizer.ERROR_CLIENT);
    }

    @Override
    protected void onCancel(Callback listener) {}

    @Override
    protected void onStopListening(Callback listener) {}
}
