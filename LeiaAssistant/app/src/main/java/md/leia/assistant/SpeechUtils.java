package md.leia.assistant;

import android.content.ComponentName;
import android.content.Context;
import android.content.Intent;
import android.content.pm.ResolveInfo;
import android.speech.RecognitionService;

import java.util.List;

public final class SpeechUtils {
    private SpeechUtils() {}

    public static ComponentName findExternalRecognizer(Context context) {
        Intent i = new Intent(RecognitionService.SERVICE_INTERFACE);
        List<ResolveInfo> list = context.getPackageManager().queryIntentServices(i, 0);
        if (list == null) return null;
        for (ResolveInfo r : list) {
            if (r.serviceInfo == null) continue;
            if (context.getPackageName().equals(r.serviceInfo.packageName)) continue;
            return new ComponentName(r.serviceInfo.packageName, r.serviceInfo.name);
        }
        return null;
    }
}
