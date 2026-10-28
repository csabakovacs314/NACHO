# NACHO privacy policy

Last updated: 27 October 2026 (version 0.10.0)

NACHO is a desktop app that runs on your own computer. It has no server and no account of its own, and its makers receive no data from it.

## Pictures and files on your computer

NACHO reads the files you give it and writes smaller copies to a folder on your computer (Pictures\Docs-Ready unless you choose another). Nothing is uploaded, except as described under Google Docs below.

## Google Docs in place

This feature is used only when you start it. NACHO asks Google for one permission, **See, edit, create and delete only the specific Google Drive files that you use with this app** (`drive.file`). With it NACHO can:

- open the Docs you pick for it in Google's file picker (or confirm there from a link you paste), read their pictures and replace them with smaller copies;
- make a copy of a Doc you picked (the default), so the original is only read: the copy is created in your Drive, in the same folder, and NACHO then changes only the copy;
- upload each smaller picture as a temporary file, share it as "anyone with the link" for the few seconds Google Docs needs to copy it, and then delete it.

NACHO cannot see any other file in your Drive. It also reads your account's e-mail address, only to show you which account is signed in.

How NACHO handles this data:

- The pictures of the Doc are downloaded to your computer, made smaller there, and put back into the same Doc. If you choose (it is on by default), copies of the original pictures are kept in Docs-Ready\Google Docs originals on your computer.
- Your sign-in (a refresh token) is stored only on your computer: encrypted for your Windows user, or on Linux in a file only your user can read. Signing out deletes it and asks Google to revoke it.
- No data from Google is sent anywhere else, sold, shared, used for advertising, or used to train any model. NACHO keeps no other copy of the text or pictures of your Docs.
- NACHO's use of information received from Google APIs adheres to the [Google API Services User Data Policy](https://developers.google.com/terms/api-services-user-data-policy), including the Limited Use requirements.

You can remove NACHO's access at any time at [myaccount.google.com](https://myaccount.google.com/connections) > Security > Third-party connections.

## Update check

Once a day (you can turn it off) NACHO asks GitHub's public API whether a newer release exists. The request carries only "NACHO/<version>" as its user agent.

## Contact

Questions: open an issue at [github.com/csabakovacs314/NACHO](https://github.com/csabakovacs314/NACHO/issues).
