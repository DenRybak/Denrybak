package com.desmond.gptwake;

import android.app.Activity;
import android.app.KeyguardManager;
import android.content.BroadcastReceiver;
import android.content.Context;
import android.content.Intent;
import android.content.IntentFilter;
import android.os.Build;
import android.os.Bundle;
import android.view.Gravity;
import android.view.WindowManager;
import android.widget.TextView;

public class UnlockRelayActivity extends Activity {
    private boolean sent;
    private BroadcastReceiver receiver;

    @Override protected void onCreate(Bundle state) {
        super.onCreate(state);
        setShowWhenLocked(true);
        setTurnScreenOn(true);
        getWindow().addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);

        TextView text = new TextView(this);
        text.setText("Ок, Лея услышала\n\nРазблокируй телефон — ChatGPT Voice запустится автоматически");
        text.setTextSize(22f);
        text.setGravity(Gravity.CENTER);
        int pad = (int)(28 * getResources().getDisplayMetrics().density);
        text.setPadding(pad, pad, pad, pad);
        setContentView(text);

        receiver = new BroadcastReceiver() {
            @Override public void onReceive(Context context, Intent intent) {
                if (Intent.ACTION_USER_PRESENT.equals(intent.getAction())) continueAfterUnlock();
            }
        };
        IntentFilter filter = new IntentFilter(Intent.ACTION_USER_PRESENT);
        if (Build.VERSION.SDK_INT >= 33) {
            registerReceiver(receiver, filter, Context.RECEIVER_NOT_EXPORTED);
        } else {
            registerReceiver(receiver, filter);
        }

        KeyguardManager km = getSystemService(KeyguardManager.class);
        if (km != null) {
            try {
                km.requestDismissKeyguard(this, new KeyguardManager.KeyguardDismissCallback() {
                    @Override public void onDismissSucceeded() { continueAfterUnlock(); }
                });
            } catch (Throwable ignored) {}
        }
    }

    @Override protected void onResume() {
        super.onResume();
        KeyguardManager km = getSystemService(KeyguardManager.class);
        if (km == null || !km.isDeviceLocked()) continueAfterUnlock();
    }

    private void continueAfterUnlock() {
        if (sent) return;
        sent = true;
        WakeController c = WakeService.controller();
        if (c != null) c.continueAfterUnlock();
        finish();
    }

    @Override protected void onDestroy() {
        if (receiver != null) {
            try { unregisterReceiver(receiver); } catch (Throwable ignored) {}
        }
        super.onDestroy();
    }
}
