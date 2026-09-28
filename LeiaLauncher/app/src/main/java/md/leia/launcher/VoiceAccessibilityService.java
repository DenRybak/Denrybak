package md.leia.launcher;

import android.accessibilityservice.AccessibilityService;
import android.accessibilityservice.GestureDescription;
import android.graphics.Path;
import android.graphics.Rect;
import android.os.Handler;
import android.os.Looper;
import android.view.accessibility.AccessibilityEvent;
import android.view.accessibility.AccessibilityNodeInfo;

import java.util.ArrayList;
import java.util.List;
import java.util.Locale;

public class VoiceAccessibilityService extends AccessibilityService {
    public static final String PREFS = "leia_prefs";
    public static final String KEY_PENDING_UNTIL = "pending_voice_until";
    private static final String CHATGPT_PACKAGE = "com.openai.chatgpt";

    private final Handler handler = new Handler(Looper.getMainLooper());
    private final Runnable attemptRunnable = this::attemptVoiceClick;
    private int attempts = 0;

    @Override
    public void onAccessibilityEvent(AccessibilityEvent event) {
        if (event == null || event.getPackageName() == null) return;
        if (!CHATGPT_PACKAGE.contentEquals(event.getPackageName())) return;
        if (!isPending()) return;

        handler.removeCallbacks(attemptRunnable);
        handler.postDelayed(attemptRunnable, 450);
    }

    @Override
    public void onInterrupt() {
        handler.removeCallbacks(attemptRunnable);
    }

    @Override
    protected void onServiceConnected() {
        super.onServiceConnected();
        attempts = 0;
    }

    private boolean isPending() {
        long until = getSharedPreferences(PREFS, MODE_PRIVATE)
                .getLong(KEY_PENDING_UNTIL, 0L);
        return System.currentTimeMillis() < until;
    }

    private void clearPending() {
        getSharedPreferences(PREFS, MODE_PRIVATE)
                .edit()
                .remove(KEY_PENDING_UNTIL)
                .apply();
        attempts = 0;
    }

    private void attemptVoiceClick() {
        if (!isPending()) return;

        AccessibilityNodeInfo root = getRootInActiveWindow();
        if (root == null) {
            retry();
            return;
        }

        Rect screen = new Rect();
        root.getBoundsInScreen(screen);

        Candidate best = new Candidate();
        scan(root, screen, best);

        if (best.node != null && best.score >= 55) {
            if (clickNodeOrParent(best.node)) {
                clearPending();
                return;
            }
        }

        // If ChatGPT exposes no useful accessibility label, choose the
        // right-most clickable control in the lower composer area.
        Candidate bottomRight = new Candidate();
        findBottomRightClickable(root, screen, bottomRight);
        if (attempts >= 2 && bottomRight.node != null) {
            if (clickNodeOrParent(bottomRight.node)) {
                clearPending();
                return;
            }
        }

        // Final fallback for layouts where the Voice icon is not represented
        // in the accessibility tree at all.
        if (attempts >= 4 && screen.width() > 0 && screen.height() > 0) {
            float x = screen.left + screen.width() * 0.91f;
            float y = screen.top + screen.height() * 0.88f;
            dispatchTap(x, y);
            clearPending();
            return;
        }

        retry();
    }

    private void retry() {
        attempts++;
        if (attempts <= 6 && isPending()) {
            handler.postDelayed(attemptRunnable, attempts < 3 ? 500 : 800);
        }
    }

    private void scan(AccessibilityNodeInfo node, Rect screen, Candidate best) {
        if (node == null) return;

        String text = normalize(node.getText());
        String desc = normalize(node.getContentDescription());
        String viewId = normalize(node.getViewIdResourceName());
        String all = (text + " " + desc + " " + viewId).trim();

        int score = 0;
        if (containsAny(all,
                "voice mode", "start voice", "start voice mode", "voice conversation",
                "голосовой режим", "голосовой разговор", "начать голос", "запустить голос",
                "voice_mode", "voicebutton", "voice_button")) {
            score += 120;
        } else {
            if (all.contains("voice")) score += 75;
            if (all.contains("голос")) score += 75;
        }

        if (containsAny(all,
                "microphone", "dictation", "dictate", "mic button",
                "микрофон", "диктов", "голосовой ввод")) {
            score -= 100;
        }

        if (node.isClickable()) score += 18;
        if (node.isEnabled()) score += 5;

        Rect r = new Rect();
        node.getBoundsInScreen(r);
        if (screen.width() > 0 && screen.height() > 0 && !r.isEmpty()) {
            float cx = r.exactCenterX();
            float cy = r.exactCenterY();
            float rightZone = screen.left + screen.width() * 0.68f;
            float lowerZone = screen.top + screen.height() * 0.68f;
            if (cx > rightZone) score += 15;
            if (cy > lowerZone) score += 15;
        }

        if (score > best.score) {
            best.score = score;
            best.node = node;
        }

        for (int i = 0; i < node.getChildCount(); i++) {
            scan(node.getChild(i), screen, best);
        }
    }

    private void findBottomRightClickable(AccessibilityNodeInfo node, Rect screen, Candidate best) {
        if (node == null || screen.width() <= 0 || screen.height() <= 0) return;

        Rect r = new Rect();
        node.getBoundsInScreen(r);
        if (node.isClickable() && node.isEnabled() && !r.isEmpty()) {
            float cx = r.exactCenterX();
            float cy = r.exactCenterY();
            float minX = screen.left + screen.width() * 0.68f;
            float minY = screen.top + screen.height() * 0.70f;

            if (cx > minX && cy > minY) {
                String all = (normalize(node.getText()) + " " +
                        normalize(node.getContentDescription()) + " " +
                        normalize(node.getViewIdResourceName())).trim();

                if (!containsAny(all,
                        "send", "отправ", "microphone", "микрофон", "dictat", "диктов",
                        "attach", "прикреп", "keyboard", "клавиат")) {
                    // Prefer farther-right controls and controls near the composer,
                    // but avoid huge containers.
                    long area = (long) r.width() * (long) r.height();
                    long screenArea = (long) screen.width() * (long) screen.height();
                    if (area > 0 && area < screenArea / 12) {
                        int score = (int) (cx * 10f + cy);
                        if (score > best.score) {
                            best.score = score;
                            best.node = node;
                        }
                    }
                }
            }
        }

        for (int i = 0; i < node.getChildCount(); i++) {
            findBottomRightClickable(node.getChild(i), screen, best);
        }
    }

    private boolean clickNodeOrParent(AccessibilityNodeInfo node) {
        AccessibilityNodeInfo current = node;
        for (int i = 0; i < 5 && current != null; i++) {
            if (current.isClickable() &&
                    current.performAction(AccessibilityNodeInfo.ACTION_CLICK)) {
                return true;
            }
            current = current.getParent();
        }

        Rect r = new Rect();
        node.getBoundsInScreen(r);
        if (!r.isEmpty()) {
            return dispatchTap(r.exactCenterX(), r.exactCenterY());
        }
        return false;
    }

    private boolean dispatchTap(float x, float y) {
        Path path = new Path();
        path.moveTo(x, y);
        GestureDescription gesture = new GestureDescription.Builder()
                .addStroke(new GestureDescription.StrokeDescription(path, 0, 80))
                .build();
        return dispatchGesture(gesture, null, null);
    }

    private static String normalize(CharSequence value) {
        if (value == null) return "";
        return value.toString()
                .toLowerCase(Locale.ROOT)
                .replace('ё', 'е')
                .trim();
    }

    private static boolean containsAny(String haystack, String... needles) {
        for (String n : needles) {
            if (haystack.contains(n)) return true;
        }
        return false;
    }

    private static class Candidate {
        AccessibilityNodeInfo node;
        int score = Integer.MIN_VALUE;
    }
}
