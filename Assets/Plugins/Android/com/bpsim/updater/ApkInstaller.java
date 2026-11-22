package com.bpsim.updater;

import android.app.Activity;
import android.content.Intent;
import android.net.Uri;
import android.os.Build;
import android.provider.Settings;

import java.io.File;

public final class ApkInstaller {
    private ApkInstaller() {}

    public static String install(Activity activity, String apkPath) {
        try {
            File apk = new File(apkPath).getCanonicalFile();
            File root = activity.getExternalFilesDir(null).getCanonicalFile();
            if (!apk.isFile() || !apk.getPath().startsWith(root.getPath() + File.separator)) {
                return "APK 文件不存在或路径不在应用私有目录中。";
            }
            if (Build.VERSION.SDK_INT >= 26 && !activity.getPackageManager().canRequestPackageInstalls()) {
                Intent settings = new Intent(Settings.ACTION_MANAGE_UNKNOWN_APP_SOURCES,
                        Uri.parse("package:" + activity.getPackageName()));
                settings.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
                activity.startActivity(settings);
                return "请在系统设置中允许本应用安装未知应用，然后返回游戏重试。";
            }

            Uri uri = Uri.parse("content://" + activity.getPackageName() + ".bpupdates/" + Uri.encode(apk.getName()));
            Intent intent = new Intent(Intent.ACTION_VIEW);
            intent.setDataAndType(uri, "application/vnd.android.package-archive");
            intent.addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION | Intent.FLAG_ACTIVITY_NEW_TASK);
            activity.startActivity(intent);
            return null;
        } catch (Exception exception) {
            return "无法启动 Android 系统安装器：" + exception;
        }
    }
}
