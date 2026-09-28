package md.leia.launcher;

import android.Manifest;
import android.app.Activity;
import android.content.Intent;
import android.content.pm.PackageManager;
import android.graphics.Color;
import android.net.Uri;
import android.os.Build;
import android.os.Bundle;
import android.provider.Settings;
import android.view.Gravity;
import android.view.ViewGroup;
import android.widget.Button;
import android.widget.LinearLayout;
import android.widget.TextView;

public class MainActivity extends Activity {
    private static final int REQ_MIC = 100;

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        setContentView(buildUi());
        requestNeededPermissions();
    }

    private LinearLayout buildUi() {
        int p = (int) (20 * getResources().getDisplayMetrics().density);
        LinearLayout root = new LinearLayout(this);
        root.setOrientation(LinearLayout.VERTICAL);
        root.setPadding(p, p, p, p);
        root.setGravity(Gravity.CENTER_HORIZONTAL);
        root.setBackgroundColor(Color.WHITE);

        TextView title = new TextView(this);
        title.setText("Ок, Лея — Voice");
        title.setTextSize(28);
        title.setTextColor(Color.BLACK);
        title.setGravity(Gravity.CENTER);
        root.addView(title, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT));

        TextView info = new TextView(this);
        info.setText(
                "ВАЖНО — один раз настрой ChatGPT:\n" +
                "ChatGPT → Настройки → Голос → «Запускать с голосом» = ВКЛ.\n\n" +
                "После этого фраза «Ок, Лея» откроет новый чат ChatGPT, а ChatGPT сам сразу запустит голосовой режим.\n\n" +
                "Лея освобождает микрофон перед запуском ChatGPT, чтобы Voice мог сразу тебя слышать."
        );
        info.setTextSize(16);
        info.setTextColor(Color.DKGRAY);
        info.setPadding(0, p, 0, p);
        root.addView(info);

        Button openChat = new Button(this);
        openChat.setText("Открыть ChatGPT для настройки");
        openChat.setOnClickListener(v -> openChatGpt());
        root.addView(openChat, lp());

        Button overlay = new Button(this);
        overlay.setText("1. Разрешить поверх других приложений");
        overlay.setOnClickListener(v -> requestOverlay());
        root.addView(overlay, lp());

        Button start = new Button(this);
        start.setText("2. Включить «Ок, Лея»");
        start.setOnClickListener(v -> startListening());
        root.addView(start, lp());

        Button test = new Button(this);
        test.setText("3. Проверить запуск Voice");
        test.setOnClickListener(v -> sendServiceAction(WakeService.ACTION_TEST_VOICE));
        root.addView(test, lp());

        Button resume = new Button(this);
        resume.setText("Возобновить прослушивание");
        resume.setOnClickListener(v -> sendServiceAction(WakeService.ACTION_RESUME));
        root.addView(resume, lp());

        Button stop = new Button(this);
        stop.setText("Остановить Лею");
        stop.setOnClickListener(v -> stopService(new Intent(this, WakeService.class)));
        root.addView(stop, lp());

        TextView hint = new TextView(this);
        hint.setText("Распознаются «Ок Лея», «Окей Лея» и близкие варианты произношения. После запуска Voice прослушивание «Ок Лея» ставится на паузу, чтобы не занимать микрофон.");
        hint.setTextSize(14);
        hint.setTextColor(Color.GRAY);
        hint.setPadding(0, p, 0, 0);
        root.addView(hint);
        return root;
    }

    private LinearLayout.LayoutParams lp() {
        LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        lp.setMargins(0, 6, 0, 6);
        return lp;
    }

    private void requestNeededPermissions() {
        if (Build.VERSION.SDK_INT >= 33 && checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS) != PackageManager.PERMISSION_GRANTED) {
            requestPermissions(new String[]{Manifest.permission.POST_NOTIFICATIONS, Manifest.permission.RECORD_AUDIO}, REQ_MIC);
        } else if (checkSelfPermission(Manifest.permission.RECORD_AUDIO) != PackageManager.PERMISSION_GRANTED) {
            requestPermissions(new String[]{Manifest.permission.RECORD_AUDIO}, REQ_MIC);
        }
    }

    private void requestOverlay() {
        if (!Settings.canDrawOverlays(this)) {
            Intent i = new Intent(Settings.ACTION_MANAGE_OVERLAY_PERMISSION, Uri.parse("package:" + getPackageName()));
            startActivity(i);
        }
    }

    private void startListening() {
        if (checkSelfPermission(Manifest.permission.RECORD_AUDIO) != PackageManager.PERMISSION_GRANTED) {
            requestPermissions(new String[]{Manifest.permission.RECORD_AUDIO}, REQ_MIC);
            return;
        }
        if (!Settings.canDrawOverlays(this)) {
            requestOverlay();
            return;
        }
        Intent i = new Intent(this, WakeService.class);
        i.setAction(WakeService.ACTION_RESUME);
        if (Build.VERSION.SDK_INT >= 26) startForegroundService(i); else startService(i);
    }

    private void sendServiceAction(String action) {
        Intent i = new Intent(this, WakeService.class);
        i.setAction(action);
        if (Build.VERSION.SDK_INT >= 26) startForegroundService(i); else startService(i);
    }

    private void openChatGpt() {
        try {
            Intent i = new Intent(Intent.ACTION_VIEW, Uri.parse("https://chatgpt.com/"));
            i.setPackage("com.openai.chatgpt");
            startActivity(i);
        } catch (Throwable t) {
            Intent launch = getPackageManager().getLaunchIntentForPackage("com.openai.chatgpt");
            if (launch != null) startActivity(launch);
        }
    }
}
