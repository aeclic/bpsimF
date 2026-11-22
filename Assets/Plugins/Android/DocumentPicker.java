package com.bpsim.documents;

import android.app.Activity;
import android.app.Fragment;
import android.content.ActivityNotFoundException;
import android.content.ContentResolver;
import android.content.Intent;
import android.database.Cursor;
import android.net.Uri;
import android.os.Bundle;
import android.provider.DocumentsContract;
import android.provider.OpenableColumns;
import java.io.File;
import java.io.FileInputStream;
import java.io.FileOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;

@SuppressWarnings("deprecation")
public final class DocumentPicker extends Fragment {
    private static final String TAG = "BpsimDocumentPicker";
    private static final int REQUEST = 1;

    public interface Result {
        void complete(String path, String error);
    }

    private Result result;
    private String action;
    private File local;
    private String mimeType;
    private String title;

    public static void launch(Activity activity, String action, String localPath,
                              String mimeType, String title, Result result) {
        activity.runOnUiThread(() -> {
            try {
                if (activity.getFragmentManager().findFragmentByTag(TAG) != null) {
                    result.complete("", "A file picker is already open.");
                    return;
                }
                DocumentPicker picker = new DocumentPicker();
                picker.action = action;
                picker.local = new File(localPath);
                picker.mimeType = mimeType;
                picker.title = title;
                picker.result = result;
                activity.getFragmentManager().beginTransaction().add(picker, TAG).commit();
            } catch (RuntimeException failure) {
                result.complete("", failure.toString());
            }
        });
    }

    @Override public void onCreate(Bundle state) {
        super.onCreate(state);
        setRetainInstance(true);
        if (result == null) {
            finish(null, null);
            return;
        }

        Intent intent = new Intent(action);
        if (!Intent.ACTION_OPEN_DOCUMENT_TREE.equals(action)) {
            intent.addCategory(Intent.CATEGORY_OPENABLE);
            intent.setType(mimeType);
        }
        if (Intent.ACTION_CREATE_DOCUMENT.equals(action)) {
            intent.putExtra(Intent.EXTRA_TITLE, title);
        }
        try {
            startActivityForResult(intent, REQUEST);
        } catch (ActivityNotFoundException failure) {
            finish(null, "No Android system file picker is available.");
        } catch (RuntimeException failure) {
            finish(null, failure.toString());
        }
    }

    @Override public void onActivityResult(int request, int status, Intent data) {
        super.onActivityResult(request, status, data);
        if (request != REQUEST || result == null) return;
        if (status != Activity.RESULT_OK) {
            finish(null, null);
            return;
        }
        Uri uri = data == null ? null : data.getData();
        if (uri == null) {
            finish(null, "No document was returned by Android.");
            return;
        }

        Activity activity = getActivity();
        ContentResolver resolver = activity.getContentResolver();
        new Thread(() -> {
            try {
                String path = transfer(resolver, uri);
                activity.runOnUiThread(() -> finish(path, null));
            } catch (Exception failure) {
                activity.runOnUiThread(() -> finish(null, failure.toString()));
            }
        }, TAG).start();
    }

    private void finish(String path, String error) {
        Result callback = result;
        result = null;
        if (getFragmentManager() != null) {
            getFragmentManager().beginTransaction().remove(this).commitAllowingStateLoss();
        }
        if (callback != null) callback.complete(path == null ? "" : path, error == null ? "" : error);
    }

    private String transfer(ContentResolver resolver, Uri uri) throws IOException {
        if (Intent.ACTION_CREATE_DOCUMENT.equals(action)) {
            try (InputStream input = new FileInputStream(local);
                 OutputStream output = resolver.openOutputStream(uri, "wt")) {
                copy(input, output);
            }
            return local.getPath();
        }
        if (Intent.ACTION_OPEN_DOCUMENT_TREE.equals(action)) {
            String root = DocumentsContract.getTreeDocumentId(uri);
            Uri document = DocumentsContract.buildDocumentUriUsingTree(uri, root);
            File target = child(local, displayName(resolver, document, "mod-folder"));
            if (!target.mkdir()) throw new IOException("Unable to create " + target.getName());
            copyDirectory(resolver, uri, root, target, 0);
            return target.getPath();
        }

        File target = child(local, displayName(resolver, uri, "document"));
        copyFile(resolver, uri, target);
        return target.getPath();
    }

    private static String displayName(ContentResolver resolver, Uri uri, String fallback) {
        String name = null;
        try (Cursor cursor = resolver.query(uri, new String[] {OpenableColumns.DISPLAY_NAME}, null, null, null)) {
            if (cursor != null && cursor.moveToFirst()) name = cursor.getString(0);
        }
        return name == null || name.isEmpty() ? fallback : name;
    }

    private static void copyDirectory(ContentResolver resolver, Uri tree, String id, File directory, int depth)
            throws IOException {
        if (depth > 64) throw new IOException("The selected folder is nested too deeply.");
        Uri children = DocumentsContract.buildChildDocumentsUriUsingTree(tree, id);
        String[] columns = {DocumentsContract.Document.COLUMN_DOCUMENT_ID,
                            DocumentsContract.Document.COLUMN_DISPLAY_NAME,
                            DocumentsContract.Document.COLUMN_MIME_TYPE};
        try (Cursor cursor = resolver.query(children, columns, null, null, null)) {
            if (cursor == null) throw new IOException("Unable to list the selected folder.");
            while (cursor.moveToNext()) {
                String childId = cursor.getString(0);
                File target = child(directory, cursor.getString(1));
                if (DocumentsContract.Document.MIME_TYPE_DIR.equals(cursor.getString(2))) {
                    if (!target.mkdir()) throw new IOException("Unable to create " + target.getName());
                    copyDirectory(resolver, tree, childId, target, depth + 1);
                } else {
                    copyFile(resolver, DocumentsContract.buildDocumentUriUsingTree(tree, childId), target);
                }
            }
        }
    }

    private static File child(File directory, String name) throws IOException {
        if (name == null || name.isEmpty() || ".".equals(name) || "..".equals(name)
                || name.indexOf('/') >= 0 || name.indexOf('\\') >= 0 || name.indexOf('\0') >= 0) {
            throw new IOException("The file provider returned an invalid name.");
        }
        return new File(directory, name);
    }

    private static void copyFile(ContentResolver resolver, Uri uri, File target) throws IOException {
        if (!target.createNewFile()) throw new IOException("Duplicate file name: " + target.getName());
        try (InputStream input = resolver.openInputStream(uri); OutputStream output = new FileOutputStream(target)) {
            copy(input, output);
        }
    }

    private static void copy(InputStream input, OutputStream output) throws IOException {
        if (input == null || output == null) throw new IOException("Unable to open the selected document.");
        byte[] buffer = new byte[64 * 1024];
        int count;
        while ((count = input.read(buffer)) != -1) output.write(buffer, 0, count);
    }
}
