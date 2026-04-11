# Atsumare Microsoft Store Submission Notes

## Product Summary

Atsumare is a Windows desktop utility that shows the user's currently open app windows, lets the user pick an app, and moves that app's windows to another monitor.

## Notes For Certification

Suggested text for Partner Center `notesForCertification`:

`Atsumare is a full-trust packaged desktop utility for Windows multi-monitor workflows. The app enumerates currently visible top-level windows on the local device so the user can choose an app and move that app's windows to another monitor. The app does not install drivers or services, does not request elevation at runtime, and does not transmit window titles, executable paths, or other device data off the device. All inspection is local and used only to render the picker UI and perform the user-requested window move action.`

## Store Listing Draft

### Short description

`Open apps, pick one, and move its windows to the monitor you want.`

### Full description

`Atsumare is a lightweight desktop utility for people who work across multiple monitors. It detects the apps you already have open, shows them in a simple launcher-style view, and lets you move an app's windows to another display with one action.`

`The app is designed for fast daily window management. Instead of dragging windows one by one, you can open Atsumare, choose the app you want, and send that app's windows to the target monitor.`

`Main features:`

- `Shows currently open desktop apps in a visual picker`
- `Groups matching windows into one app tile when appropriate`
- `Uses app icons so the picker is easy to scan`
- `Moves the selected app's windows to another monitor`
- `Supports settings for startup and picker behavior`

### Features

- `Move an app's windows to another monitor in one action`
- `See open apps in a compact visual picker`
- `Designed for multi-monitor desktop workflows`
- `Keeps processing local to the device`

## Support Info

Recommended Partner Center property values:

- `Website`: your product page or repository home page
- `Support contact`: support email or support page URL
- `Privacy policy URL`: hosted copy of `docs/privacy-policy.md`
- `License terms`: paste or adapt `docs/terms-of-use.md`

## Submission Checklist

- `Build Release`, not Debug
- Ensure debug ribbon and diagnostic tile text are not shown in `Release`
- Verify `runFullTrust` is still required and reflected in certification notes
- Verify no runtime UAC prompt appears
- Verify no driver or service installation logic is present in shipped package
- Verify privacy policy URL is public and reachable over HTTPS
- Verify support page or support email is public
- Verify screenshots reflect the non-debug UI
- Verify Store listing does not claim unsupported behavior
