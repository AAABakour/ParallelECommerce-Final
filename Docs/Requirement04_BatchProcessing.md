# Requirement 04 - Batch Processing

## الهدف

الهدف من هذا المتطلب هو تنفيذ وظيفة خلفية `Background Job` تقوم بجرد المبيعات اليومية ومعالجة البيانات على شكل دفعات `Chunks` بدلاً من معالجة كل سجل بشكل منفرد ومتسلسل.

في نظام التجارة الإلكترونية، هذا النوع من المعالجة يستخدم في مهام مثل:

- جرد المبيعات اليومية.
- حساب إجمالي الإيرادات.
- إنشاء تقارير يومية.
- تجهيز بيانات التحليلات.
- معالجة عدد كبير من الطلبات القديمة بدون تعطيل المستخدمين.

---

## المشكلة

إذا تمت معالجة كل سجل مبيعات بشكل متسلسل، فإن زمن التنفيذ يزيد خطياً مع عدد السجلات.

مثلاً إذا كان كل سجل يحتاج 30ms:

```text
100 records × 30ms ≈ 3000ms
```

وهذا يصبح أسوأ مع آلاف أو ملايين السجلات.

---

## الحلول المقترحة

### 1. المعالجة المتسلسلة Sequential Processing

كل سجل ينتظر انتهاء السجل السابق.

**الميزة:** بسيطة وسهلة الفهم.

**العيب:** بطيئة جداً عند زيادة البيانات.

---

### 2. معالجة كل السجلات بالتوازي دفعة واحدة

تشغيل كل السجلات معاً باستخدام Tasks كثيرة.

**الميزة:** أسرع ظاهرياً.

**العيب:** قد تستهلك الموارد بشكل مفرط إذا كان عدد السجلات كبيراً.

---

### 3. تقسيم البيانات إلى Chunks ومعالجة الدفعات بالتوازي مع حد آمن

تقسيم البيانات إلى دفعات، ثم معالجة عدد محدود من الدفعات بالتوازي.

**الميزة:** توازن بين السرعة والتحكم بالموارد.

**العيب:** يحتاج بنية أكثر تنظيماً.

---

## الحل المختار

تم اختيار الحل الثالث:

```text
Parallel Chunk Batch Processing with bounded max parallel chunks
```

أي:

```text
Large daily sales records
        ↓
Split into chunks
        ↓
Process chunks in parallel
        ↓
Limit max active chunks with SemaphoreSlim
        ↓
Store final job result
```

السبب أن هذا الحل يحقق شرطين معاً:

1. زيادة سرعة معالجة البيانات الضخمة.
2. عدم فتح عدد غير محدود من العمليات المتوازية.

---

## ما تمت إضافته في الكود

### Models

```text
Models/BatchJobRequest.cs
Models/BatchJobSnapshot.cs
Models/BatchChunkSnapshot.cs
Models/BatchMetricsSnapshot.cs
```

### Services

```text
Services/BatchProcessingService.cs
Services/BatchJobWorkerService.cs
```

### Endpoints

```http
POST /batch/reset
GET  /batch/metrics
GET  /batch/jobs
GET  /batch/jobs/{jobId}
POST /batch/before/sequential
POST /batch/after/start-daily-sales-job
```

وتم الإبقاء على endpoints القديمة لأغراض الـ Demo:

```http
POST /demo/batch/before
POST /demo/batch/after
```

---

## Before - Sequential Batch Processing

Endpoint:

```http
POST /batch/before/sequential
```

أو للتجربة السريعة:

```http
POST /demo/batch/before
```

هذه النسخة تعالج السجلات واحداً تلو الآخر.

### النتيجة المتوقعة

```json
{
  "mode": "BEFORE - Sequential Batch Processing",
  "totalRecords": 100,
  "processedRecords": 100,
  "totalSales": 42425,
  "durationMs": 3000,
  "problem": "Records were processed one by one."
}
```

الزمن قد يختلف حسب الجهاز، لكن من المتوقع أن يكون بحدود 3 ثوانٍ عند 100 سجل.

---

## After - Parallel Chunk Background Job

Endpoint:

```http
POST /batch/after/start-daily-sales-job
```

هذا endpoint لا يعالج كل شيء داخل request نفسه، بل ينشئ Job ويرجعه مباشرة بحالة:

```text
Queued
```

ثم يقوم:

```text
BatchJobWorkerService
```

بالتقاط الـ Job ومعالجته بالخلفية.

يمكن متابعة حالة الـ Job من:

```http
GET /batch/jobs/{jobId}
```

ويمكن قراءة المقاييس من:

```http
GET /batch/metrics
```

---

## لماذا هذا يحقق شرط Background Job؟

لأن الطلب الرئيسي لا ينتظر انتهاء الجرد اليومي بالكامل.

بدلاً من ذلك:

```text
Client starts job
        ↓
API returns 202 Accepted with jobId
        ↓
BackgroundService processes chunks
        ↓
Client checks status later
```

وهذا يشبه طريقة عمل أنظمة التجارة الإلكترونية الحقيقية: التقارير والجرد لا يجب أن يعطلوا مسار المستخدمين.

---

## التحكم بالموارد داخل Batch

حتى لا تتحول المعالجة المتوازية إلى استهلاك مفرط للموارد، تم وضع حد أعلى لعدد الدفعات التي تعمل بنفس اللحظة:

```text
maxParallelChunks = 4
```

وتقيس الخدمة:

```text
maxActiveChunksObserved
```

لإثبات أن عدد الـ chunks النشطة لم يتجاوز الحد المسموح.

---

## طريقة الاختبار اليدوي من Swagger

1. تصفير القياسات:

```http
POST /batch/reset
```

2. تشغيل المعالجة المتسلسلة:

```http
POST /batch/before/sequential
```

3. تشغيل وظيفة الخلفية:

```http
POST /batch/after/start-daily-sales-job
```

4. نسخ `jobId` من الاستجابة.

5. متابعة حالة الوظيفة:

```http
GET /batch/jobs/{jobId}
```

6. قراءة المقاييس:

```http
GET /batch/metrics
```

---

## ما الصور المطلوبة لاحقاً؟

سنأخذ الصور النهائية بعد تثبيت أول 5 متطلبات كلها:

```text
Docs/Screenshots/04_batch_before_sequential.png
Docs/Screenshots/04_batch_after_job_queued.png
Docs/Screenshots/04_batch_after_job_completed.png
Docs/Screenshots/04_batch_metrics.png
```

---

## الخلاصة

تم تقوية الطلب الرابع بحيث لم يعد مجرد دالة تقسم البيانات إلى chunks، بل أصبح عندنا:

- معالجة قبل التحسين Sequential.
- معالجة بعد التحسين باستخدام Chunks.
- Background Job حقيقي عبر `BackgroundService`.
- Job status يمكن تتبعه.
- Metrics توضح حالة الوظائف وعدد الـ chunks النشطة.
- حد آمن للتوازي حتى لا يتم استهلاك الموارد بشكل مفتوح.

هذا يحقق متطلب `Batch Processing` بصورة أوضح وأكثر قابلية للشرح والاختبار.
