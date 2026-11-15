package com.takemetothemoon.debug;

import android.content.BroadcastReceiver;
import android.content.Context;
import android.content.Intent;
import android.util.Log;
import java.util.concurrent.ArrayBlockingQueue;

/** Explicit ADB broadcasts only; manifest requires the sender's DUMP permission. */
public final class MoonDebugReceiver extends BroadcastReceiver {
    private static final ArrayBlockingQueue<String> commands = new ArrayBlockingQueue<>(16);
    private static volatile boolean ready;
    public static void setReady(boolean value) { ready = value; if (!value) commands.clear(); }
    public static String poll() { return commands.poll(); }
    @Override public void onReceive(Context context, Intent intent) {
        String command = intent.getStringExtra("command");
        if (!"reset".equals(command) && !"prologue".equals(command) &&
            !"ending-success".equals(command) && !"stage1".equals(command) && !"stage3".equals(command) && !"skip".equals(command) && !"status".equals(command)) {
            setResultCode(1); setResultData("Unknown command: reset, prologue, stage1, stage3, ending-success, skip, status"); return;
        }
        if (!ready) { setResultCode(2); setResultData("Open the app with remote debug commands enabled first"); return; }
        boolean accepted = commands.offer(command);
        setResultCode(accepted ? 0 : 3);
        setResultData(accepted ? "Queued: " + command + "; check logcat MoonDebug/Unity for completion" : "Command queue full");
        Log.i("MoonDebug", "Received " + command + ", queued=" + accepted);
    }
}
