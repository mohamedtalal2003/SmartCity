# شرح كل ما بنيناه حتى الآن (إصلاح S3 + المرحلة M0)

هذا الملف يشرح بالعربية: ماذا يوجد في كل مجلد، ولماذا، وكيف تعمل فحوصات الصحة (Health Checks)،
وكيف اختبرنا كل شيء — مع أوامر تستطيع تشغيلها بنفسك لترى النتيجة بعينك.

> المصطلحات التقنية (أسماء الملفات، الأوامر، أسماء المكتبات) تركناها بالإنجليزية كما هي، لأنك ستراها هكذا في الكود.

---

## 1. الفكرة العامة في جملتين

سيارات البلدية فيها كاميرات ترسل صوراً إلى السحابة، والذكاء الاصطناعي يكتشف مشاكل الطرق (حُفر، لافتات مكسورة...)
ثم تتحول إلى أوامر عمل لفرق الصيانة.

نحن نبني **"هيكلاً يمشي" (Walking Skeleton)**: كل الخدمات موجودة وتعمل وتتصل ببعضها من البداية للنهاية،
لكن المنطق الداخلي بسيط جداً في البداية. المرحلة **M0** هدفها فقط: **أن تقوم كل الخدمات وتكون "بصحة جيدة" ومتصلة بالبنية التحتية.**
لا يوجد منطق أعمال بعد.

---

## 2. خريطة المجلدات

```
smartcity-skeleton/
├── docker-compose.yml          ← البنية التحتية (قواعد بيانات، رسائل، تخزين صور، سجلات)
├── infra/
│   ├── postgres/init/          ← سكربت ينشئ قاعدة بيانات منفصلة لكل خدمة
│   └── s3/init-buckets.sh      ← سكربت ينشئ "حاويات" الصور (buckets) ومدة انتهاء الصور
├── SmartCity.sln               ← ملف الحل: يجمع كل المشاريع لتُبنى بأمر واحد
├── Directory.Build.props       ← إعدادات مشتركة لكل المشاريع (.NET 8 ...)
├── Directory.Packages.props    ← إصدارات كل المكتبات في مكان واحد
├── scripts/                    ← سكربتات تشغيل وإيقاف كل الخدمات دفعة واحدة
├── src/
│   ├── SmartCity.Contracts/        ← "العقود": شكل الرسائل والأحداث (موجود من قبل، لم نلمسه)
│   ├── SmartCity.Contracts.Grpc/   ← كود gRPC مولَّد تلقائياً من ملفات .proto
│   ├── BuildingBlocks/
│   │   └── SmartCity.BuildingBlocks/   ← "السباكة" المشتركة التي تستعملها كل خدمة
│   ├── Services/                   ← الخدمات التسع (كل خدمة مشروع مستقل)
│   └── Gateway/SmartCity.ApiGateway/  ← البوابة: نقطة الدخول الوحيدة للمتصفح
├── docs/                       ← وثائق التصميم وخطة البناء وهذا الملف
├── tests/                      ← (فارغ الآن، الاختبارات تأتي في M3 و M5)
└── tools/                      ← (فارغ الآن، محاكي السيارات يأتي في M1)
```

### 2.1 `docker-compose.yml` — البنية التحتية

هذه برامج جاهزة تعمل داخل Docker، وخدماتنا تتصل بها:

| الحاوية | ماذا تفعل | المنفذ |
|---|---|---|
| `postgres` | قاعدة البيانات (مع PostGIS للخرائط) | 5432 |
| `rabbitmq` | "ساعي البريد" بين الخدمات: خدمة تنشر حدثاً، والخدمات المهتمة تستقبله | 5672 (واجهة: 15672) |
| `emqx` | يستقبل رسائل السيارات عبر بروتوكول MQTT | 1883 |
| `s3` | تخزين الصور (SeaweedFS، متوافق مع Amazon S3) | 9000 |
| `s3-init` | يعمل مرة واحدة: ينشئ حاويات الصور ثم يخرج | — |
| `redis` | ذاكرة سريعة (مواقع السيارات الحية لاحقاً) | 6379 |
| `seq` | يجمع سجلات (logs) كل الخدمات في مكان واحد للبحث فيها | 5341 (واجهة: 8081) |

### 2.2 `infra/` — سكربتات التهيئة

- **`postgres/init/01-create-databases.sql`**: ينشئ قاعدة بيانات ومستخدماً **لكل خدمة** (`pothole_db`, `workorder_db` ...).
  القاعدة الذهبية: **كل خدمة تقرأ وتكتب في قاعدتها فقط**، لا تلمس قاعدة غيرها أبداً.
- **`s3/init-buckets.sh`**: ينشئ ثلاث حاويات للصور:
  - `frames-temp` — الصور الواردة من السيارات. **تُحذف تلقائياً بعد 30 يوماً** (قاعدة Lifecycle).
  - `frames-permanent` — الصور التي نريد الاحتفاظ بها.
  - `evidence` — صور إثبات إنجاز الصيانة.

### 2.3 `src/SmartCity.Contracts.Grpc` — كود gRPC

الخدمات ستعرض بياناتها للبوابة عبر **gRPC** (طريقة سريعة للاتصال بين البرامج). شكل هذه الواجهات مكتوب في ملفات `.proto`
داخل مشروع العقود. هذا المشروع **لا يحتوي كوداً كتبناه بأيدينا**: الأداة `Grpc.Tools` تقرأ ملفات `.proto` وتولّد كود C# تلقائياً عند البناء.

### 2.4 `src/BuildingBlocks/SmartCity.BuildingBlocks` — السباكة المشتركة

بدلاً من أن تكرر كل خدمة نفس الإعدادات عشر مرات، كتبناها مرة واحدة هنا. كل خدمة تستدعيها بسطر واحد.

| الملف | الدالة | ماذا تفعل ببساطة |
|---|---|---|
| `Logging/LoggingExtensions.cs` | `AddSmartCityLogging` | كل سطر سجل يُطبع على الشاشة **ويُرسل إلى Seq**، ومعه اسم الخدمة (`Service`). |
| `Messaging/BusExtensions.cs` | `AddSmartCityBus` | يربط الخدمة بـ RabbitMQ عبر مكتبة MassTransit. إذا فشلت معالجة رسالة يعيد المحاولة بعد 1 ثم 2 ثم 4 ثوانٍ، وبعدها ينقلها إلى طابور `_error`. |
| `Messaging/CorrelationIdLogFilter.cs` | (مرشّح) | عند استقبال أي رسالة، يضيف `CorrelationId` لكل سطر سجل. هذا ما يسمح لاحقاً بتتبع صورة واحدة عبر كل الخدمات. |
| `Messaging/OutboxExtensions.cs` | `AddSmartCityOutbox` | "صندوق الصادر": الحفظ في قاعدة البيانات ونشر الحدث يحدثان **معاً أو لا يحدثان** — لا يضيع حدث ولا يتكرر. (سيُستعمل من M2.) |
| `Messaging/PublishExtensions.cs` | `PublishFollowUp`, `PublishEvent` | طريقة النشر الوحيدة المسموحة. ترفض النشر إذا نسيت نسخ `CorrelationId`، وتضبط `MessageId = EventId` لمنع التكرار. |
| `Hosting/HealthCheckExtensions.cs` | `AddSmartCityHealthChecks` | فحوصات الصحة — مشروحة بالتفصيل في القسم 3. |
| `Hosting/KestrelExtensions.cs` | `ConfigureSmartCityKestrel` | يفتح منفذين لكل خدمة: واحد HTTP/1 (للصحة والـ REST) وواحد HTTP/2 (لـ gRPC). |
| `Storage/BlobStorageExtensions.cs` | `AddSmartCityBlobStorage`, `EnsureBucketsAsync` | يتصل بتخزين الصور. `EnsureBucketsAsync` ينشئ الحاويات الناقصة **فقط في بيئة التطوير** ولا يغيّر قواعد الانتهاء أبداً. |

### 2.5 `src/Services/*` و `src/Gateway` — الخدمات

كل خدمة الآن "قشرة فارغة" — `Program.cs` قصير جداً. مثال (خدمة الحُفر):

```csharp
const string ServiceName = "Pothole";

var builder = WebApplication.CreateBuilder(args);
builder.AddSmartCityLogging(ServiceName);   // السجلات → شاشة + Seq
builder.ConfigureSmartCityKestrel();        // المنفذان 5230 و 5231
builder.AddSmartCityBus(ServiceName);       // الاتصال بـ RabbitMQ
builder.AddSmartCityHealthChecks();         // فحص قاعدة البيانات
builder.Services.AddGrpc();

var app = builder.Build();
app.MapSmartCityHealthChecks();             // /health/live و /health/ready
app.Run();
```

وفي كل مجلد خدمة:
- `appsettings.json` — إعدادات عامة بلا أسرار (المنافذ، عنوان RabbitMQ، عنوان Seq).
- `appsettings.Development.json` — كلمات المرور وسلاسل الاتصال **لبيئة التطوير المحلية فقط**.
- `Properties/launchSettings.json` — يجعل `dotnet run` يعمل في بيئة `Development`.

| الخدمة | منفذ HTTP | منفذ gRPC | قاعدة بيانات | RabbitMQ |
|---|---|---|---|---|
| ApiGateway | 5000 | — | — | **لا** (البوابة تتكلم gRPC فقط) |
| VehicleCommunication | 5210 | — | — | نعم |
| AiDetection | 5220 | — | — | نعم |
| Pothole | 5230 | 5231 | pothole_db | نعم |
| Inventory | 5240 | 5241 | inventory_db | نعم |
| Violation | 5250 | 5251 | violation_db | نعم |
| Building | 5260 | 5261 | building_db | نعم |
| CostCalculation | 5270 | 5271 | costcalc_db | نعم |
| WorkOrder | 5280 | 5281 | workorder_db | نعم |
| Notification | 5290 | 5291 | notification_db | نعم |

### 2.6 `scripts/` — التشغيل والإيقاف

| الملف | لمن |
|---|---|
| `run-all.ps1` / `stop-all.ps1` | ويندوز (PowerShell) |
| `run-all.sh` / `stop-all.sh` | لينكس (مثلاً جلسات السحابة) |

`run-all` يبني الحل ثم يشغّل الخدمات العشر في الخلفية. يحفظ:
- سجل كل خدمة في `.run/logs/<اسم المشروع>.log`
- رقم العملية (PID) في `.run/pids/<اسم المشروع>.pid` — هكذا يعرف `stop-all` ماذا يوقف.

مجلد `.run/` موجود في `.gitignore` فلا يُرفع إلى GitHub.

---

## 3. فحوصات الصحة (Health Checks) — كيف تعمل؟

### 3.1 الفكرة

كل خدمة تعرض عنوانين يمكن لأي أحد (أنت، أو Docker، أو Kubernetes لاحقاً) أن يسألهما: "هل أنتِ بخير؟"

| العنوان | السؤال الذي يجيب عنه | ماذا يفحص |
|---|---|---|
| `/health/live` | "هل البرنامج نفسه يعمل؟" | **لا شيء** — إذا أجاب أصلاً فهو حي. |
| `/health/ready` | "هل أنتِ جاهزة لاستقبال عمل؟" | كل اعتمادياتها: قاعدة البيانات + RabbitMQ. |

الجواب كلمة واحدة مع رمز HTTP:
- `Healthy` مع **200** ← كل شيء سليم.
- `Unhealthy` مع **503** ← شيء ما معطّل.

### 3.2 ماذا يفحص `/health/ready` في كل خدمة؟

الكود في `Hosting/HealthCheckExtensions.cs` يسجّل الفحوصات، وكل فحص عليه وسم (tag) اسمه `ready`:

1. **فحص Postgres** — يُضاف فقط إذا كان للخدمة سلسلة اتصال `ConnectionStrings:Db`.
   يفتح اتصالاً بقاعدة الخدمة وينفّذ `SELECT 1`.
2. **فحص RabbitMQ** — تضيفه مكتبة MassTransit تلقائياً (سمّيناه `rabbitmq`) ووسمناه `ready`.
   يكون سليماً عندما يكون "الناقل" (bus) متصلاً بـ RabbitMQ.

ثم `MapSmartCityHealthChecks` يقول:
- `/health/live` ← لا تشغّل أي فحص (`Predicate = _ => false`).
- `/health/ready` ← شغّل كل فحص عليه وسم `ready`.

لذلك:
| الخدمة | ما يفحصه `/health/ready` |
|---|---|
| Pothole, Inventory, Violation, Building, CostCalculation, WorkOrder, Notification | Postgres **و** RabbitMQ |
| VehicleCommunication, AiDetection | RabbitMQ فقط (لا قاعدة بيانات لهما) |
| ApiGateway | لا شيء بعد (Redis سيُضاف في M4) |

### 3.3 مشكلة وجدناها وأصلحناها

لاختبار أن `/health/ready` **صادق فعلاً** (وليس يقول Healthy دائماً)، أوقفنا Postgres مؤقتاً (`docker compose pause postgres`)
وسألنا خدمة الحُفر.

- **قبل الإصلاح:** الطلب **علِق** أكثر من 15 ثانية بلا جواب. السبب: مكتبة Npgsql تنتظر مدتها الافتراضية
  (15 ثانية للاتصال، 30 ثانية للاستعلام) ولا تتوقف عند انتهاء مهلة فحص الصحة.
- **الإصلاح:** أعطينا فحص الصحة نسخته الخاصة من سلسلة الاتصال بمهلة 3 ثوانٍ (`Timeout = 3, CommandTimeout = 3`).
  اتصال الخدمة الحقيقي لم يتغيّر.
- **بعد الإصلاح:** الجواب `Unhealthy (503)` خلال ~6 ثوانٍ، ويعود `Healthy (200)` فور عودة Postgres.
  وخدمة AiDetection (بلا قاعدة بيانات) بقيت `Healthy` — وهذا صحيح.

---

## 4. كيف اختبرنا M0؟ (شروط "Done when" من خطة البناء)

خطة البناء (`docs/BUILD-PLAN.md`) تضع أربعة شروط لنجاح M0. هذه هي، وكيف تتحقق منها بنفسك.

### الخطوة 0: شغّل البنية التحتية

```powershell
docker compose up -d
docker compose ps -a
```
يجب أن ترى كل شيء `(healthy)`، و `s3-init` حالته `Exited (0)` (أي أنهى عمله بنجاح). `seq` لا يملك فحص صحة لذا يظهر `Up` فقط.

### الشرط 1: البناء بلا أخطاء

```powershell
dotnet build SmartCity.sln
```
النتيجة المطلوبة: `0 Error(s)`. (حصلنا على 0 أخطاء و 0 تحذيرات.)

### الشرط 2: كل خدمة تقوم و `/health/ready` يقول Healthy

```powershell
.\scripts\run-all.ps1

# اسأل كل خدمة:
foreach ($p in 5000,5210,5220,5230,5240,5250,5260,5270,5280,5290) {
  $r = Invoke-WebRequest "http://localhost:$p/health/ready" -UseBasicParsing
  "$p -> $($r.Content) ($($r.StatusCode))"
}
```
أو افتح في المتصفح مثلاً: `http://localhost:5230/health/ready`

النتيجة المطلوبة: `Healthy (200)` للعشرة. ✅

**جرّب بنفسك أن الفحص صادق:**
```powershell
docker compose pause postgres
Invoke-WebRequest http://localhost:5230/health/ready -UseBasicParsing   # → خطأ 503 Unhealthy
docker compose unpause postgres
Invoke-WebRequest http://localhost:5230/health/ready -UseBasicParsing   # → Healthy
```

**منافذ gRPC:** لا يمكن فحصها بالمتصفح (تحتاج HTTP/2)، فتأكدنا أنها "تستمع":
```powershell
Get-NetTCPConnection -State Listen -LocalPort 5231,5241,5251,5261,5271,5281,5291
```

### الشرط 3: RabbitMQ يُظهر اتصالاً لكل خدمة

افتح `http://localhost:15672` (المستخدم/كلمة المرور: `smartcity` / `smartcity`) ← تبويب **Connections**.

النتيجة: **9 اتصالات**، كل اتصال باسم خدمته (`SmartCity.Pothole.Api` ...). ✅
البوابة ليس لها اتصال **عن قصد** — قاعدة المعمارية تقول: البوابة تتكلم مع الخدمات عبر gRPC فقط، لا عبر الرسائل.

### الشرط 4: Seq يُظهر سجلات بدء كل خدمة

افتح `http://localhost:8081` واكتب في البحث:
```
Service = 'Pothole'
```
سترى: `Now listening on: http://localhost:5230` و `Bus started: rabbitmq://localhost/` ...

ولرؤية كل الخدمات معاً، في تبويب الاستعلام:
```
select count(*) from stream group by Service
```
النتيجة: الخدمات العشر كلها ظاهرة. ✅ وعدد التحذيرات/الأخطاء: صفر (عدا خطأ واحد متوقع من اختبار إيقاف Postgres).

### أخيراً: أوقف الخدمات

```powershell
.\scripts\stop-all.ps1
```

---

## 5. ما فعلناه قبل M0: إصلاح حاوية التخزين S3

**المشكلة:** حاوية `s3` كانت دائماً `unhealthy`.

**السبب:** فحص الصحة في `docker-compose.yml` كان يسأل `http://localhost:8333/`. داخل الحاوية، كلمة `localhost`
تُترجم أولاً إلى عنوان IPv6 (`::1`)، لكن SeaweedFS يستمع على IPv4 فقط ← "Connection refused" في كل مرة،
رغم أن الخدمة نفسها كانت تعمل جيداً.

**الإصلاح:** غيّرنا العنوان إلى `127.0.0.1` (IPv4 صريح).

**وأضفنا:** حاوية `s3-init` تعمل مرة واحدة بعد أن تصبح `s3` سليمة، وتشغّل `infra/s3/init-buckets.sh` الذي:
1. ينشئ الحاويات الثلاث (وإن كانت موجودة يتجاهل بهدوء).
2. يضع قاعدة: الصور في `frames-temp` تنتهي بعد **30 يوماً** (أمر S3 قياسي، سيعمل على Amazon S3 أيضاً).

**كيف تتحقق بنفسك:**
```powershell
curl.exe -I http://localhost:9000/frames-temp            # 200 = الحاوية موجودة
curl.exe "http://localhost:9000/frames-temp?lifecycle"   # يُظهر <Days>30</Days>
```

---

## 6. قرارات مهمة يجب أن تعرفها

1. **MassTransit 8.4.1 وليس أحدث.** إصدار 9 تجاري مدفوع، وإصدارات 8.5 تتطلب EF Core 9، بينما مشروعنا على EF Core 8.
2. **في بيئة التطوير الخدمات تستمع على localhost فقط** — حتى لا يسألك جدار حماية ويندوز عن كل خدمة.
3. **`EnsureBucketsAsync` يُستدعى في VehicleCommunication** من الآن (الخطة تضعه في M1)، لنتأكد أنه يعمل.
4. **مدة 30 يوماً للصور المؤقتة مؤقتة** — مُعلَّمة بـ `SKELETON:` في `init-buckets.sh`. القرار النهائي للإنتاج ما زال مفتوحاً.

---

## 7. ملخص الأوامر

```powershell
docker compose up -d                 # تشغيل البنية التحتية
docker compose ps -a                 # التأكد أنها سليمة
dotnet build SmartCity.sln           # بناء كل شيء
.\scripts\run-all.ps1                # تشغيل الخدمات العشر
.\scripts\stop-all.ps1               # إيقافها
Get-Content .run\logs\SmartCity.Pothole.Api.log -Tail 20   # آخر سجلات خدمة معينة
```

| الواجهة | العنوان | الدخول |
|---|---|---|
| RabbitMQ | http://localhost:15672 | smartcity / smartcity |
| Seq (السجلات) | http://localhost:8081 | — |
| EMQX | http://localhost:18083 | admin / public |

**المرحلة التالية (M1):** محاكي سيارات يرسل صوراً ← خدمة VehicleCommunication تحفظها في `frames-temp` وتنشر حدث `FrameReceived`.
