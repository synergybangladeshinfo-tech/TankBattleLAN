# GitHub দিয়ে APK বানানোর নিয়ম (কম্পিউটারে Unity লাগবে না)

ক্লাউডে (GitHub Actions + GameCI) APK তৈরি হবে। সব সেটআপ ফাইল প্রজেক্টে দেওয়া আছে — আপনার কাজ শুধু ৪টা ধাপ:

## ধাপ ১: দুটো ফ্রি অ্যাকাউন্ট

- **GitHub অ্যাকাউন্ট**: github.com → Sign up
- **Unity অ্যাকাউন্ট (Unity ID)**: id.unity.com → Create account (ফ্রি Personal লাইসেন্সের জন্য লাগবে)

## ধাপ ২: প্রজেক্ট GitHub-এ আপলোড

1. github.com-এ লগইন → ডানদিকে **+** → **New repository**
2. নাম দিন `TankBattleLAN`, **Public** সিলেক্ট করুন → **Create repository**
3. রিপোতে **uploading an existing file** লিংকে ক্লিক করুন
4. আপনার কম্পিউটারের `TankBattleLAN` ফোল্ডারের **ভেতরের সবকিছু** ড্র্যাগ-ড্রপ করুন (`Assets`, `Packages`, `ProjectSettings`, `.github` ফোল্ডারসহ) → **Commit changes**
   - সতর্কতা: `.github` ফোল্ডারটা হিডেন থাকতে পারে — File Explorer-এ *View → Show → Hidden items* চালু করুন। ওয়েব আপলোডে ফোল্ডার-স্ট্রাকচার না উঠলে GitHub Desktop অ্যাপ ব্যবহার করুন (সহজতম)।

## ধাপ ৩: Unity লাইসেন্স Secret যোগ করুন

রিপোর পেজে: **Settings → Secrets and variables → Actions → New repository secret**

| Secret নাম | মান |
|---|---|
| `UNITY_EMAIL` | আপনার Unity ID-র ইমেইল |
| `UNITY_PASSWORD` | Unity ID-র পাসওয়ার্ড |

(এতে কাজ না হলে GameCI-র ম্যানুয়াল অ্যাক্টিভেশন লাগবে: game.ci/docs/github/activation দেখুন — `UNITY_LICENSE` নামে তৃতীয় secret যোগ করতে হয়।)

## ধাপ ৪: বিল্ড চালান ও APK নামান

1. রিপোর **Actions** ট্যাব → বাঁয়ে **Build Android APK** → **Run workflow**
2. প্রথমবার ৩০–৬০ মিনিট লাগতে পারে (পরেরবার ক্যাশের কারণে দ্রুত)
3. শেষ হলে সবুজ টিক → রানটা খুলুন → নিচে **Artifacts** → **TankBattleLAN-APK** ডাউনলোড করুন
4. Zip খুলে `TankBattleLAN.apk` সব ফোনে ইনস্টল করুন — খেলা শুরু!

## সমস্যা হলে

- **"unityVersion image not found"** এরর: `ProjectSettings/ProjectVersion.txt`-এ ভার্সনটা এমন একটা 6000.0.x ভার্সনে বদলান যেটা hub.docker.com/r/unityci/editor/tags তালিকায় আছে।
- **License activation failed**: ধাপ ৩-এর ম্যানুয়াল পদ্ধতি (game.ci/docs/github/activation) অনুসরণ করুন।
- Actions ফ্রি: Public রিপোতে আনলিমিটেড বিল্ড মিনিট।
