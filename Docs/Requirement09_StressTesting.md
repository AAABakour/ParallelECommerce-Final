# Requirement 09 - Stress Testing: 100 Concurrent Users

## المطلوب

إثبات أن النظام قادر على خدمة **100 مستخدم متزامن على الأقل** بدون انهيار وبدون فقدان أو تخريب بيانات. الاختبار لا يجب أن يكون مجرد طلب واحد، بل يجب أن يمر على العمليات الأساسية في المنظومة.

## الفكرة الهندسية

تم تنفيذ اختبارين متكاملين:

1. **اختبار داخلي قابل للتشغيل من Swagger** عبر endpoint واحد:
   - `POST /demo/stress/100-users`
   - هذا يشغل 100 virtual users داخل التطبيق، وكل مستخدم ينفذ رحلة تشمل عمليات القراءة، المخزون، الكاش، الطابور، batch، توزيع الأحمال، distributed lock، وtransaction integrity.

2. **اختبار خارجي عبر JMeter**:
   - الملف موجود داخل المشروع:
     - `Tests/JMeter/Requirement09_Stress100Users_AllOperations.jmx`
   - هذا الملف يشغل Thread Group من 100 مستخدم ويستدعي endpoints حقيقية عبر HTTP.

## العمليات التي يغطيها اختبار الـ 100 مستخدم

كل مستخدم افتراضي ينفذ العمليات التالية:

| العملية | الهدف |
|---|---|
| Redis cache product read | قياس القراءة المتكررة من الكاش بدل قاعدة البيانات |
| Redis cache popular products | اختبار المنتجات الأكثر طلباً |
| Redis cache stock snapshot | اختبار كاش المخزون القصير العمر |
| Safe inventory purchase | التأكد من عدم حدوث race condition في خصم المخزون |
| Capacity-controlled heavy operation | التأكد أن النظام لا يفتح عمليات ثقيلة بلا حدود |
| Async notification enqueue | نقل العمل البطيء إلى queue |
| Batch parallel chunks | اختبار معالجة دفعات صغيرة بالتوازي |
| Load-balancer round robin | توزيع الطلبات على عدة nodes افتراضية |
| Redis distributed lock coupon | حماية كوبون محدود الاستخدام |
| Redis distributed lock payment | منع تكرار احتساب نفس عملية الدفع |
| ACID checkout transaction | ضمان أن الطلب والدفع والمخزون متناسقة |

## لماذا توجد business failures متوقعة؟

ليس كل فشل في الاختبار يعني أن النظام فشل. بعض النتائج هي فشل تجاري صحيح وآمن:

- الكوبون `FLASH-100` متاح فقط لـ 5 استخدامات. لذلك من الطبيعي أن ينجح 5 فقط ويفشل الباقي برسالة sold out.
- الدفع يستخدم 10 payment references، وكل reference تصل له عدة callbacks. الصحيح أن يتم capture مرة واحدة فقط لكل reference.
- checkout transaction يملك 5 قطع فقط في مخزونه الداخلي، لذلك 5 طلبات تنجح والباقي تفشل بسبب نفاد المخزون بدون تخريب بيانات.

المهم هو:

- `UnexpectedFailures = 0`
- `StabilityPassed = true`
- `DataIntegrityValidated = true`

## Endpoints المستخدمة

### Reset

```http
POST /stress-test/reset
```

### تشغيل اختبار 100 مستخدم

```http
POST /demo/stress/100-users
```

### قراءة النتائج النهائية

```http
GET /stress-test/metrics
```

### مخطط موارد النظام

```http
GET /stress-test/resources/svg
```

### بيانات الموارد الخام

```http
GET /monitoring/resources
GET /monitoring/resources/csv
```

## خطوات التشغيل

```powershell
docker compose -f docker-compose.cache.yml up -d
docker exec -it parallel-ecommerce-redis redis-cli ping
dotnet restore
dotnet run
```

ثم افتح:

```text
http://localhost:5164/swagger
```

إذا ظهر port مختلف في terminal، استخدمه بدلاً من 5164.

## خطوات الاختبار من Swagger

1. نفّذ:

```http
POST /stress-test/reset
```

صورة مقترحة:

```text
Req09_01_Reset.png
```

2. نفّذ:

```http
POST /demo/stress/100-users
```

صورة مقترحة:

```text
Req09_02_Stress_100_Users_Result.png
```

يجب أن تظهر القيم التالية:

```json
"configuredConcurrentUsers": 100,
"completedUsers": 100,
"unexpectedFailures": 0,
"stabilityPassed": true,
"dataIntegrityValidated": true
```

3. نفّذ:

```http
GET /stress-test/metrics
```

صورة مقترحة:

```text
Req09_03_Final_Metrics.png
```

4. افتح:

```http
GET /stress-test/resources/svg
```

صورة مقترحة:

```text
Req09_04_Resource_Chart.png
```

5. افتح:

```http
GET /monitoring/resources/csv
```

واحفظه كدليل إضافي أو ارسمه في Excel/Grafana.

صورة مقترحة:

```text
Req09_05_Resources_CSV.png
```

## خطوات JMeter لاحقاً

افتح JMeter ثم:

```text
File > Open > Tests/JMeter/Requirement09_Stress100Users_AllOperations.jmx
```

تأكد أن:

```text
Server Name: localhost
Port: 5164
Threads: 100
Ramp-up: 5 seconds
Loop Count: 1
```

ثم شغّل الاختبار وخذ صوراً من:

- Summary Report
- View Results Tree
- Aggregate Report

أسماء الصور المقترحة:

```text
Req09_06_JMeter_Summary_Report.png
Req09_07_JMeter_View_Results_Tree.png
Req09_08_JMeter_Aggregate_Report.png
```

## معيار القبول

يعتبر الطلب التاسع ناجحاً إذا تحققت هذه الشروط:

```text
CompletedUsers = 100
UnexpectedFailures = 0
StabilityPassed = true
DataIntegrityValidated = true
No application crash
No data loss
```

## الخلاصة

تم بناء اختبار احترافي للضغط يشمل 100 مستخدم متزامن ويمر على أغلب المكونات التي تم تنفيذها في الطلبات السابقة. الاختبار يميز بين فشل تجاري متوقع وآمن وبين خطأ نظامي حقيقي، ويقدم أيضاً ملخصاً لاستهلاك الموارد مع الزمن عبر resource samples وSVG chart.
