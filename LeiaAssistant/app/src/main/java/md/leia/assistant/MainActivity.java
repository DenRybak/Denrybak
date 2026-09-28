package md.leia.assistant;

import android.Manifest;
import android.app.Activity;
import android.app.role.RoleManager;
import android.content.ComponentName;
import android.content.Intent;
import android.content.pm.PackageManager;
import android.graphics.Color;
import android.os.Build;
import android.os.Bundle;
import android.service.voice.VoiceInteractionService;
import android.text.InputType;
import android.view.Gravity;
import android.view.ViewGroup;
import android.widget.Button;
import android.widget.CheckBox;
import android.widget.EditText;
import android.widget.LinearLayout;
import android.widget.TextView;
import android.widget.Toast;

import java.util.ArrayList;
import java.util.List;

public class MainActivity extends Activity {
    private static final int REQ_PERMS = 20;
    private static final int REQ_ROLE = 21;

    private TextView assistantStatus;
    private TextView gptStatus;
    private CheckBox wakeCheck;
    private EditText apiKey;

    @Override
    protected void onCreate(Bundle state) {
        super.onCreate(state);
        setContentView(buildUi());
    }

    @Override
    protected void onResume() {
        super.onResume();
        refresh();
    }

    private LinearLayout buildUi() {
        int p = (int)(20 * getResources().getDisplayMetrics().density);
        LinearLayout root = new LinearLayout(this);
        root.setOrientation(LinearLayout.VERTICAL);
        root.setPadding(p, p, p, p);
        root.setBackgroundColor(Color.WHITE);

        TextView title = new TextView(this);
        title.setText("Лея Assistant");
        title.setTextSize(30);
        title.setTextColor(Color.BLACK);
        title.setGravity(Gravity.CENTER);
        root.addView(title, full());

        TextView info = new TextView(this);
        info.setText("Полноценный системный ассистент Android.\n\n" +
                "Умеет: голосовой диалог, звонки по контактам, запуск приложений, таймеры, будильники, фонарик и навигацию. " +
                "Обычные вопросы отправляются в OpenAI.");
        info.setTextSize(16);
        info.setTextColor(Color.DKGRAY);
        info.setPadding(0, p, 0, p);
        root.addView(info, full());

        assistantStatus = new TextView(this);
        assistantStatus.setTextSize(17);
        assistantStatus.setGravity(Gravity.CENTER);
        root.addView(assistantStatus, full());

        Button role = new Button(this);
        role.setText("1. Назначить Лею системным ассистентом");
        role.setOnClickListener(v -> requestAssistantRole());
        root.addView(role, full());

        Button perms = new Button(this);
        perms.setText("2. Разрешить функции телефона");
        perms.setOnClickListener(v -> requestNeededPermissions());
        root.addView(perms, full());

        wakeCheck = new CheckBox(this);
        wakeCheck.setText("Слушать фразу «Ок, Лея» в фоне");
        wakeCheck.setTextSize(16);
        wakeCheck.setOnCheckedChangeListener((b, checked) -> {
            getSharedPreferences(LeiaVoiceInteractionService.PREFS, MODE_PRIVATE)
                    .edit().putBoolean(LeiaVoiceInteractionService.KEY_WAKE_ENABLED, checked).apply();
            if (checked) {
                sendBroadcast(new Intent(LeiaVoiceInteractionService.ACTION_RESUME_WAKE)
                        .setPackage(getPackageName()));
            }
        });
        root.addView(wakeCheck, full());

        TextView keyLabel = new TextView(this);
        keyLabel.setText("Подключение GPT");
        keyLabel.setTextSize(18);
        keyLabel.setTextColor(Color.BLACK);
        keyLabel.setPadding(0, p, 0, 5);
        root.addView(keyLabel, full());

        apiKey = new EditText(this);
        apiKey.setHint("OpenAI API key");
        apiKey.setSingleLine(true);
        apiKey.setInputType(InputType.TYPE_CLASS_TEXT | InputType.TYPE_TEXT_VARIATION_PASSWORD);
        root.addView(apiKey, full());

        Button saveKey = new Button(this);
        saveKey.setText("Сохранить ключ в защищённом хранилище");
        saveKey.setOnClickListener(v -> saveApiKey());
        root.addView(saveKey, full());

        gptStatus = new TextView(this);
        gptStatus.setTextSize(15);
        gptStatus.setGravity(Gravity.CENTER);
        gptStatus.setPadding(0, 5, 0, p);
        root.addView(gptStatus, full());

        Button test = new Button(this);
        test.setText("3. Запустить Лею");
        test.setOnClickListener(v -> launchAssistant());
        root.addView(test, full());

        TextView note = new TextView(this);
        note.setText("Примеры: «Позвони Лене», «Открой YouTube», «Таймер на 10 минут», " +
                "«Включи фонарик», «Веди до аэропорта», либо задай обычный вопрос.");
        note.setTextSize(14);
        note.setTextColor(Color.GRAY);
        note.setPadding(0, p, 0, 0);
        root.addView(note, full());

        return root;
    }

    private LinearLayout.LayoutParams full() {
        LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        lp.setMargins(0, 5, 0, 5);
        return lp;
    }

    private void refresh() {
        ComponentName service = new ComponentName(this, LeiaVoiceInteractionService.class);
        boolean active = VoiceInteractionService.isActiveService(this, service);
        assistantStatus.setText(active
                ? "✓ Лея выбрана системным ассистентом"
                : "✗ Лея пока не системный ассистент");
        assistantStatus.setTextColor(active ? 0xFF168A2E : 0xFFC62828);

        wakeCheck.setOnCheckedChangeListener(null);
        wakeCheck.setChecked(getSharedPreferences(LeiaVoiceInteractionService.PREFS, MODE_PRIVATE)
                .getBoolean(LeiaVoiceInteractionService.KEY_WAKE_ENABLED, true));
        wakeCheck.setOnCheckedChangeListener((b, checked) -> {
            getSharedPreferences(LeiaVoiceInteractionService.PREFS, MODE_PRIVATE)
                    .edit().putBoolean(LeiaVoiceInteractionService.KEY_WAKE_ENABLED, checked).apply();
            if (checked) sendBroadcast(new Intent(LeiaVoiceInteractionService.ACTION_RESUME_WAKE)
                    .setPackage(getPackageName()));
        });

        gptStatus.setText(SecureKeyStore.has(this)
                ? "✓ OpenAI подключён"
                : "GPT пока не подключён — команды телефону уже работают.");
        gptStatus.setTextColor(SecureKeyStore.has(this) ? 0xFF168A2E : 0xFF666666);
    }

    private void requestAssistantRole() {
        RoleManager rm = getSystemService(RoleManager.class);
        if (rm != null && rm.isRoleAvailable(RoleManager.ROLE_ASSISTANT)) {
            startActivityForResult(rm.createRequestRoleIntent(RoleManager.ROLE_ASSISTANT), REQ_ROLE);
        } else {
            startActivity(new Intent("android.settings.VOICE_INPUT_SETTINGS"));
        }
    }

    private void requestNeededPermissions() {
        List<String> p = new ArrayList<>();
        addIfMissing(p, Manifest.permission.RECORD_AUDIO);
        addIfMissing(p, Manifest.permission.READ_CONTACTS);
        addIfMissing(p, Manifest.permission.CALL_PHONE);
        addIfMissing(p, Manifest.permission.CAMERA);
        if (Build.VERSION.SDK_INT >= 33) addIfMissing(p, Manifest.permission.POST_NOTIFICATIONS);
        if (!p.isEmpty()) requestPermissions(p.toArray(new String[0]), REQ_PERMS);
        else Toast.makeText(this, "Все нужные разрешения уже выданы", Toast.LENGTH_SHORT).show();
    }

    private void addIfMissing(List<String> list, String p) {
        if (checkSelfPermission(p) != PackageManager.PERMISSION_GRANTED) list.add(p);
    }

    private void saveApiKey() {
        String key = apiKey.getText().toString().trim();
        if (key.isBlank()) return;
        try {
            SecureKeyStore.save(this, key);
            apiKey.setText("");
            Toast.makeText(this, "OpenAI подключён", Toast.LENGTH_SHORT).show();
            refresh();
        } catch (Throwable t) {
            Toast.makeText(this, "Не удалось сохранить ключ", Toast.LENGTH_LONG).show();
        }
    }

    private void launchAssistant() {
        if (!VoiceInteractionService.isActiveService(this,
                new ComponentName(this, LeiaVoiceInteractionService.class))) {
            Toast.makeText(this, "Сначала назначь Лею системным ассистентом", Toast.LENGTH_LONG).show();
            requestAssistantRole();
            return;
        }
        try {
            Intent i = new Intent(Intent.ACTION_ASSIST);
            i.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
            startActivity(i);
        } catch (Throwable t) {
            Toast.makeText(this, "Используй системный жест/кнопку вызова ассистента", Toast.LENGTH_LONG).show();
        }
    }
}
