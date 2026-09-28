package com.desmond.gptwake;

import android.accessibilityservice.AccessibilityService;
import android.accessibilityservice.GestureDescription;
import android.content.Context;
import android.graphics.Path;
import android.graphics.Rect;
import android.os.Handler;
import android.os.Looper;
import android.view.accessibility.AccessibilityEvent;
import android.view.accessibility.AccessibilityNodeInfo;
import java.util.Locale;

public class VoiceClickService extends AccessibilityService {
    private static final String PREFS = "voice_click";
    private static final String KEY_UNTIL = "pending_until";
    private static final String CHATGPT = "com.openai.chatgpt";
    private final Handler handler = new Handler(Looper.getMainLooper());
    private int attempts;

    public static void arm(Context c) {
        c.getSharedPreferences(PREFS, Context.MODE_PRIVATE)
                .edit().putLong(KEY_UNTIL, System.currentTimeMillis() + 15000L).apply();
    }

    private boolean pending() {
        return System.currentTimeMillis() < getSharedPreferences(PREFS, MODE_PRIVATE)
                .getLong(KEY_UNTIL, 0L);
    }

    private void clear() {
        getSharedPreferences(PREFS, MODE_PRIVATE).edit().remove(KEY_UNTIL).apply();
        attempts = 0;
    }

    @Override public void onAccessibilityEvent(AccessibilityEvent event) {
        if (event == null || event.getPackageName() == null) return;
        if (!CHATGPT.contentEquals(event.getPackageName()) || !pending()) return;
        handler.removeCallbacksAndMessages(null);
        handler.postDelayed(this::attempt, 450);
    }

    @Override public void onInterrupt() { handler.removeCallbacksAndMessages(null); }

    private void attempt() {
        if (!pending()) return;
        AccessibilityNodeInfo root = getRootInActiveWindow();
        if (root == null) { retry(); return; }

        Rect screen = new Rect();
        root.getBoundsInScreen(screen);
        Candidate best = new Candidate();
        scan(root, screen, best);
        if (best.node != null && best.score >= 60 && click(best.node)) { clear(); return; }

        Candidate br = new Candidate();
        findBottomRight(root, screen, br);
        if (attempts >= 2 && br.node != null && click(br.node)) { clear(); return; }

        if (attempts >= 4 && screen.width() > 0 && screen.height() > 0) {
            tap(screen.left + screen.width() * 0.91f, screen.top + screen.height() * 0.88f);
            clear();
            return;
        }
        retry();
    }

    private void retry() {
        attempts++;
        if (attempts <= 7 && pending()) handler.postDelayed(this::attempt, attempts < 3 ? 500 : 800);
    }

    private void scan(AccessibilityNodeInfo node, Rect screen, Candidate best) {
        if (node == null) return;
        String all = (norm(node.getText())+" "+norm(node.getContentDescription())+" "+
                norm(node.getViewIdResourceName())).trim();
        int score = 0;
        if (contains(all,"voice mode","start voice","voice conversation","голосовой режим",
                "голосовой разговор","начать голос","voice_mode","voicebutton","voice_button")) score += 120;
        else {
            if (all.contains("voice")) score += 75;
            if (all.contains("голос")) score += 75;
        }
        if (contains(all,"microphone","dictation","микрофон","диктов","голосовой ввод")) score -= 100;
        if (node.isClickable()) score += 18;
        if (node.isEnabled()) score += 5;
        Rect r = new Rect();
        node.getBoundsInScreen(r);
        if (!r.isEmpty() && screen.width()>0 && screen.height()>0) {
            if (r.exactCenterX() > screen.left + screen.width()*0.68f) score += 15;
            if (r.exactCenterY() > screen.top + screen.height()*0.68f) score += 15;
        }
        if (score > best.score) { best.score=score; best.node=node; }
        for(int i=0;i<node.getChildCount();i++) scan(node.getChild(i),screen,best);
    }

    private void findBottomRight(AccessibilityNodeInfo node, Rect screen, Candidate best) {
        if (node==null || screen.width()<=0 || screen.height()<=0) return;
        Rect r=new Rect(); node.getBoundsInScreen(r);
        if(node.isClickable() && node.isEnabled() && !r.isEmpty()) {
            float cx=r.exactCenterX(), cy=r.exactCenterY();
            if(cx>screen.left+screen.width()*0.68f && cy>screen.top+screen.height()*0.70f) {
                String all=(norm(node.getText())+" "+norm(node.getContentDescription())+" "+norm(node.getViewIdResourceName())).trim();
                if(!contains(all,"send","отправ","microphone","микрофон","dictat","диктов","attach","прикреп","keyboard","клавиат")) {
                    long area=(long)r.width()*r.height(), sa=(long)screen.width()*screen.height();
                    if(area>0 && area<sa/12) {
                        int score=(int)(cx*10+cy);
                        if(score>best.score){best.score=score;best.node=node;}
                    }
                }
            }
        }
        for(int i=0;i<node.getChildCount();i++) findBottomRight(node.getChild(i),screen,best);
    }

    private boolean click(AccessibilityNodeInfo node) {
        AccessibilityNodeInfo cur=node;
        for(int i=0;i<5 && cur!=null;i++) {
            if(cur.isClickable() && cur.performAction(AccessibilityNodeInfo.ACTION_CLICK)) return true;
            cur=cur.getParent();
        }
        Rect r=new Rect(); node.getBoundsInScreen(r);
        return !r.isEmpty() && tap(r.exactCenterX(),r.exactCenterY());
    }

    private boolean tap(float x,float y) {
        Path p=new Path(); p.moveTo(x,y);
        GestureDescription g=new GestureDescription.Builder()
                .addStroke(new GestureDescription.StrokeDescription(p,0,80)).build();
        return dispatchGesture(g,null,null);
    }

    private static String norm(CharSequence s) {
        return s==null?"":s.toString().toLowerCase(Locale.ROOT).replace('ё','е').trim();
    }
    private static boolean contains(String h,String... ns) {
        for(String n:ns) if(h.contains(n)) return true;
        return false;
    }
    private static class Candidate { AccessibilityNodeInfo node; int score=Integer.MIN_VALUE; }
}
