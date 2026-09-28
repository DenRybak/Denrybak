package md.leia.launcher;

import android.Manifest;
import android.app.Activity;
import android.content.ComponentName;
import android.content.Intent;
import android.content.pm.PackageManager;
import android.graphics.Color;
import android.net.Uri;
import android.os.Build;
import android.os.Bundle;
import android.provider.Settings;
import android.text.TextUtils;
import android.view.Gravity;
import android.view.ViewGroup;
import android.widget.Button;
import android.widget.LinearLayout;
import android.widget.TextView;
import android.widget.Toast;

public class MainActivity extends Activity {
    private static final int REQ_MIC = 100;
    private TextView status;

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        setContentView(buildUi());
        requestNeededPermissions();
    }

    @Override
    protected void onResume() {
        super.onResume();
        if (status != null) {
            status.setText(isAccessibilityEnabled()
                    ? "✓ Спец. возможности включены — можно тестировать"
                    : "✗ Сначала включи службу «Ок Лея — управление ChatGPT»");
            status.setTextColor(isAccessibilityEnabled() ? 0xFF168A2E : 0xFFC62828);
        }
    }

    private LinearLayout buildUi() {
        int p = (int) (18 * getResources().getDisplayMetrics().density);
        LinearLayout root = new LinearLayout(this);
        root.setOrientation(LinearLayout.VERTICAL);
        root.setPadding(p, p, p, p);
        root.setGravity(Gravity.CENTER_HORIZONTAL);
        root.setBackgroundColor(Color.WHITE);

        TextView title = new TextView(this);
        title.setText("Ок, Лея — Voice v4");
        title.setTextSize(27);
        title.setTextColor(Color.BLACK);
        title.setGravity(Gravity.CENTER);
        root.addView(title, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT));

        TextView info = new TextView(this);
        info.setText(
                "Эта версия не использует deep-link Voice. Она открывает ChatGPT и сама нажимает кнопку голосового режима через Спец. возможности Android.\n\n" +
                "Служба ограничена только приложением ChatGPT."
        );
        info.setTextSize(16);
        info.setTextColor(Color.DKGRAY);
        info.setPadding(0, p, 0, p / 2);
        root.addView(info);

        status = new TextView(this);
        status.setTextSize(16);
        status.setGravity(Gravity.CENTER);
        status.setPadding(0, 0, 0, p);
        root.addView(status, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT));

        Button accessibility = new Button(this);
        accessibility.setText("1. Включить Спец. возможности для Ок Лея");
        accessibility.setOnClickListener(v -> openAccessibilitySettings());
        root.addView(accessibility, lp());

        Button overlay = new Button(this);
        overlay.setText("2. Разрешить поверх других приложений");
        overlay.setOnClickListener(v -> requestOverlay());
        root.addView(overlay, lp());

        Button start = new Button(this);
        start.setText("3. Включить «Ок, Лея»");
        start.setOnClickListener(v -> startListening());
        root.addView(start, lp());

        Button test = new Button(this);
        test.setText("4. Проверить запуск Voice");
        test.setOnClickListener(v -> testVoice());
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
        hint.setText("Тест должен открыть ChatGPT и примерно через 1–2 секунды автоматически нажать Voice. Если появится запрос Android на разрешение службы — разреши его.");
        hint.setTextSize(14);
        hint.setTextColor(Color.GRAY);
        hint.setPadding(0, p, 0, 0);
        root.addView(hint);

        return root;
    }

    private LinearLayout.LayoutParams lp() {
        LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        lp.setMargins(0, 5, 0, 5);
        return lp;
    }

    private void requestNeededPermissions() {
        if (Build.VERSION.SDK_INT >= 33 && checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS) != PackageManager.PERMISSION_GRANTED) {
            requestPermissions(new String[]{Manifest.permission.POST_NOTIFICATIONS, Manifest.permission.RECORD_AUDIO}, REQ_MIC);
        } else if (checkSelfPermission(Manifest.permission.RECORD_AUDIO) != PackageManager.PERMISSION_GRANTED) {
            requestPermissions(new String[]{Manifest.permission.RECORD_AUDIO}, REQ_MIC);
        }
    }

    private void openAccessibilitySettings() {
        try {
            startActivity(new Intent(Settings.ACTION_ACCESSIBILITY_SETTINGS));
        } catch (Throwable t) {
            startActivity(new Intent(Settings.ACTION_SETTINGS));
        }
    }

    private boolean isAccessibilityEnabled() {
        ComponentName expected = new ComponentName(this, VoiceAccessibilityService.class);
        String enabled = Settings.Secure.getString(
                getContentResolver(),
                Settings.Secure.ENABLED_ACCESSIBILITY_SERVICES);
        if (enabled == null) return false;

        TextUtils.SimpleStringSplitter splitter = new TextUtils.SimpleStringSplitter(':');
        splitter.setString(enabled);
        while (splitter.hasNext()) {
            ComponentName cn = ComponentName.unflattenFromString(splitter.next());
            if (expected.equals(cn)) return true;
        }
        return false;
    }

    private void requestOverlay() {
        if (!Settings.canDrawOverlays(this)) {
            Intent i = new Intent(Settings.ACTION_MANAGE_OVERLAY_PERMISSION, Uri.parse("package:" + getPackageName()));
            startActivity(i);
        }
    }

    private void startListening() {
        if (!isAccessibilityEnabled()) {
            Toast.makeText(this, "Сначала включи службу в Спец. возможностях", Toast.LENGTH_LONG).show();
            openAccessibilitySettings();
            return;
        }
        if (checkSelfPermission(Manifest.permission.RECORD_AUDIO) != PackageManager.PERMISSION_GRANTED) {
            requestPermissions(new String[]{Manifest.permission.RECORD_AUDIO}, REQ_MIC);
            return;
        }
        if (!Settings.canDrawOverlays(this)) {
            requestOverlay();
            return;
        }
        sendServiceAction(WakeService.ACTION_RESUME);
    }

    private void testVoice() {
        if (!isAccessibilityEnabled()) {
            Toast.makeText(this, "Сначала включи «Ок Лея — управление ChatGPT»", Toast.LENGTH_LONG).show();
            openAccessibilitySettings();
            return;
        }
        sendServiceAction(WakeService.ACTION_TEST_VOICE);
    }

    private void sendServiceAction(String action) {
        Intent i = new Intent(this, WakeService.class);
        i.setAction(action);
        if (Build.VERSION.SDK_INT >= 26) startForegroundService(i); else startService(i);
    }
}
