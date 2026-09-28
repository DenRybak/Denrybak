package md.leia.assistant;

import android.Manifest;
import android.app.SearchManager;
import android.content.Context;
import android.content.Intent;
import android.content.pm.ApplicationInfo;
import android.content.pm.PackageManager;
import android.database.Cursor;
import android.hardware.camera2.CameraCharacteristics;
import android.hardware.camera2.CameraManager;
import android.net.Uri;
import android.provider.AlarmClock;
import android.provider.ContactsContract;

import java.util.List;
import java.util.Locale;
import java.util.regex.Matcher;
import java.util.regex.Pattern;

public final class CommandRouter {
    public interface Callback {
        void reply(String text);
    }

    private CommandRouter() {}

    public static void execute(Context c, String original, Callback cb) {
        String s = normalize(original);

        if (s.startsWith("позвони ") || s.startsWith("набери ")) {
            String name = s.replaceFirst("^(позвони|набери)\\s+", "").trim();
            callContact(c, name, cb);
            return;
        }

        if (s.startsWith("открой ") || s.startsWith("запусти ")) {
            String app = s.replaceFirst("^(открой|запусти)\\s+", "").trim();
            if (openApp(c, app)) cb.reply("Открываю " + app + ".");
            else cb.reply("Не нашла приложение «" + app + "».");
            return;
        }

        if (s.contains("фонарик")) {
            boolean off = s.contains("выключ") || s.contains("отключ");
            if (setTorch(c, !off)) cb.reply(off ? "Фонарик выключен." : "Фонарик включён.");
            else cb.reply("Не удалось переключить фонарик.");
            return;
        }

        if (s.contains("таймер")) {
            int seconds = parseDurationSeconds(s);
            if (seconds > 0) {
                Intent i = new Intent(AlarmClock.ACTION_SET_TIMER)
                        .putExtra(AlarmClock.EXTRA_LENGTH, seconds)
                        .putExtra(AlarmClock.EXTRA_MESSAGE, "Лея")
                        .putExtra(AlarmClock.EXTRA_SKIP_UI, true)
                        .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
                try {
                    c.startActivity(i);
                    cb.reply("Таймер запущен.");
                } catch (Throwable t) {
                    cb.reply("Не удалось запустить таймер.");
                }
                return;
            }
        }

        if (s.contains("будильник")) {
            Matcher m = Pattern.compile("(\\d{1,2})[:.](\\d{2})").matcher(s);
            if (m.find()) {
                int h = Integer.parseInt(m.group(1));
                int min = Integer.parseInt(m.group(2));
                Intent i = new Intent(AlarmClock.ACTION_SET_ALARM)
                        .putExtra(AlarmClock.EXTRA_HOUR, h)
                        .putExtra(AlarmClock.EXTRA_MINUTES, min)
                        .putExtra(AlarmClock.EXTRA_MESSAGE, "Лея")
                        .putExtra(AlarmClock.EXTRA_SKIP_UI, true)
                        .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
                try {
                    c.startActivity(i);
                    cb.reply(String.format(Locale.ROOT, "Будильник на %02d:%02d установлен.", h, min));
                } catch (Throwable t) {
                    cb.reply("Не удалось установить будильник.");
                }
                return;
            }
        }

        if (s.startsWith("маршрут ") || s.startsWith("навигация ") ||
                s.startsWith("поехали ") || s.startsWith("веди ")) {
            String q = s.replaceFirst("^(маршрут|навигация|поехали|веди)\\s+(до\\s+|в\\s+|к\\s+)?", "").trim();
            if (!q.isBlank()) {
                Uri uri = Uri.parse("google.navigation:q=" + Uri.encode(q));
                Intent i = new Intent(Intent.ACTION_VIEW, uri);
                i.setPackage("com.google.android.apps.maps");
                i.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
                try {
                    c.startActivity(i);
                    cb.reply("Строю маршрут до " + q + ".");
                } catch (Throwable t) {
                    Intent fallback = new Intent(Intent.ACTION_VIEW,
                            Uri.parse("geo:0,0?q=" + Uri.encode(q))).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
                    try {
                        c.startActivity(fallback);
                        cb.reply("Открываю маршрут.");
                    } catch (Throwable ignored) {
                        cb.reply("Не удалось открыть навигацию.");
                    }
                }
                return;
            }
        }

        GptClient.ask(c, original, new GptClient.Callback() {
            @Override public void onResult(String text) { cb.reply(text); }
            @Override public void onError(String error) { cb.reply(error); }
        });
    }

    private static String normalize(String s) {
        return s == null ? "" : s.toLowerCase(Locale.ROOT)
                .replace('ё', 'е')
                .replaceAll("\\s+", " ")
                .trim();
    }

    private static int parseDurationSeconds(String s) {
        Matcher m = Pattern.compile("(\\d+)\\s*(сек|секунд|мин|минут|час|часа|часов)").matcher(s);
        if (!m.find()) return -1;
        int n = Integer.parseInt(m.group(1));
        String unit = m.group(2);
        if (unit.startsWith("час")) return n * 3600;
        if (unit.startsWith("мин")) return n * 60;
        return n;
    }

    private static void callContact(Context c, String query, Callback cb) {
        if (query.isBlank()) {
            cb.reply("Кому позвонить?");
            return;
        }
        if (c.checkSelfPermission(Manifest.permission.READ_CONTACTS) != PackageManager.PERMISSION_GRANTED) {
            cb.reply("Разреши Лее доступ к контактам.");
            return;
        }

        String number = null;
        String display = null;
        Cursor cursor = null;
        try {
            cursor = c.getContentResolver().query(
                    ContactsContract.CommonDataKinds.Phone.CONTENT_URI,
                    new String[]{
                            ContactsContract.CommonDataKinds.Phone.DISPLAY_NAME,
                            ContactsContract.CommonDataKinds.Phone.NUMBER
                    },
                    ContactsContract.CommonDataKinds.Phone.DISPLAY_NAME + " LIKE ?",
                    new String[]{"%" + query + "%"},
                    ContactsContract.CommonDataKinds.Phone.DISPLAY_NAME + " ASC");
            if (cursor != null && cursor.moveToFirst()) {
                display = cursor.getString(0);
                number = cursor.getString(1);
            }
        } catch (Throwable ignored) {
        } finally {
            if (cursor != null) cursor.close();
        }

        if (number == null) {
            cb.reply("Не нашла контакт «" + query + "».");
            return;
        }

        Intent i;
        if (c.checkSelfPermission(Manifest.permission.CALL_PHONE) == PackageManager.PERMISSION_GRANTED) {
            i = new Intent(Intent.ACTION_CALL, Uri.parse("tel:" + Uri.encode(number)));
        } else {
            i = new Intent(Intent.ACTION_DIAL, Uri.parse("tel:" + Uri.encode(number)));
        }
        i.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
        try {
            c.startActivity(i);
            cb.reply("Звоню " + display + ".");
        } catch (Throwable t) {
            cb.reply("Не удалось начать звонок.");
        }
    }

    private static boolean openApp(Context c, String query) {
        PackageManager pm = c.getPackageManager();
        Intent main = new Intent(Intent.ACTION_MAIN).addCategory(Intent.CATEGORY_LAUNCHER);
        List<android.content.pm.ResolveInfo> apps = pm.queryIntentActivities(main, 0);
        for (android.content.pm.ResolveInfo r : apps) {
            String label = r.loadLabel(pm).toString().toLowerCase(Locale.ROOT);
            if (label.contains(query) || query.contains(label)) {
                Intent launch = pm.getLaunchIntentForPackage(r.activityInfo.packageName);
                if (launch != null) {
                    launch.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
                    c.startActivity(launch);
                    return true;
                }
            }
        }
        return false;
    }

    private static boolean setTorch(Context c, boolean enabled) {
        if (c.checkSelfPermission(Manifest.permission.CAMERA) != PackageManager.PERMISSION_GRANTED) return false;
        try {
            CameraManager cm = c.getSystemService(CameraManager.class);
            for (String id : cm.getCameraIdList()) {
                Boolean flash = cm.getCameraCharacteristics(id).get(CameraCharacteristics.FLASH_INFO_AVAILABLE);
                Integer facing = cm.getCameraCharacteristics(id).get(CameraCharacteristics.LENS_FACING);
                if (Boolean.TRUE.equals(flash) &&
                        (facing == null || facing == CameraCharacteristics.LENS_FACING_BACK)) {
                    cm.setTorchMode(id, enabled);
                    return true;
                }
            }
        } catch (Throwable ignored) {}
        return false;
    }
}
