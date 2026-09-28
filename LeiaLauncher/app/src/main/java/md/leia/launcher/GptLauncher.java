package md.leia.launcher;

import android.content.ComponentName;
import android.content.Context;
import android.content.Intent;
import android.content.pm.ActivityInfo;
import android.content.pm.PackageManager;
import android.net.Uri;

public final class GptLauncher {
    private static final String GPT_PACKAGE = "com.openai.chatgpt";
    private static final ComponentName GPT_VOICE_COMPONENT = new ComponentName(
            GPT_PACKAGE, "com.openai.voice.assistant.AssistantActivity");

    public enum Result {
        DIRECT,
        DEEPLINK,
        FAILED
    }

    private GptLauncher() {}

    public static Result launch(Context context) {
        if (launchDirect(context)) return Result.DIRECT;
        if (launchDeeplink(context)) return Result.DEEPLINK;
        return Result.FAILED;
    }

    private static boolean launchDirect(Context context) {
        try {
            PackageManager pm = context.getPackageManager();
            ActivityInfo info = pm.getActivityInfo(GPT_VOICE_COMPONENT, 0);
            if (!info.exported) return false;
            if (info.permission != null) return false;

            Intent intent = new Intent()
                    .setComponent(GPT_VOICE_COMPONENT)
                    .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
            context.startActivity(intent);
            return true;
        } catch (Throwable t) {
            return false;
        }
    }

    private static boolean launchDeeplink(Context context) {
        try {
            Intent intent = new Intent(Intent.ACTION_VIEW, Uri.parse("https://chat.com/?mode=voice"))
                    .setPackage(GPT_PACKAGE)
                    .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
            context.startActivity(intent);
            return true;
        } catch (Throwable t) {
            return false;
        }
    }
}
