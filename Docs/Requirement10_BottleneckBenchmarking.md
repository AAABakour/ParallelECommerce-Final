# Requirement 10 - Bottleneck Analysis & Benchmarking

## المطلوب
قياس زمن الاستجابة لأهم عمليات النظام، تحديد اختناق واحد على الأقل، ثم تقديم مقارنة رقمية قبل التحسين وبعده.

## الفكرة
تم بناء خدمة Benchmark داخلية تقيس أهم العمليات التي تؤثر على الأداء:

1. قراءة تفاصيل المنتج.
2. قراءة المنتجات الأكثر طلباً.
3. قراءة لقطة المخزون.
4. معالجة جرد المبيعات اليومي.

تم قياس كل عملية في مرحلتين:

- **Before:** تنفيذ مباشر بدون التحسين المناسب، مثل القراءة المباشرة من مصدر البيانات أو المعالجة التسلسلية.
- **After:** تنفيذ بعد التحسين، مثل Redis Cache أو Parallel Chunk Processing.

## الاختناق المحدد
الاختناق الرئيسي المتوقع يظهر في واحدة من العمليتين:

- **Daily sales batch calculation:** عند التنفيذ التسلسلي، لأن كل record ينتظر السابق.
- **Popular products query:** لأنها query متكررة ومكلفة وتقرأ نفس البيانات كثيراً.

الخدمة تحدد الاختناق رقمياً بعد تشغيل الاختبار حسب أعلى زمن متوسط قبل التحسين.

## الحلول المستخدمة

### Redis Cache
استخدمنا Redis لتقليل زمن العمليات المتكررة:

- Product details.
- Popular products.
- Stock snapshot.

### Parallel Chunk Processing
تم تقسيم معالجة البيانات إلى chunks وتنفيذها بشكل متوازي مع حد أعلى للتوازي، حتى نسرّع المعالجة بدون استهلاك زائد للموارد.

## Endpoints

```http
POST /benchmark/reset
POST /demo/benchmark/before
POST /demo/benchmark/after
POST /demo/benchmark/full
GET  /benchmark/metrics
GET  /benchmark/report
GET  /benchmark/chart/svg
```

## طريقة الاختبار من Swagger

1. تشغيل Redis:

```powershell
docker compose -f docker-compose.cache.yml up -d
docker exec -it parallel-ecommerce-redis redis-cli ping
```

2. تشغيل المشروع:

```powershell
dotnet run
```

3. فتح Swagger:

```text
http://localhost:5164/swagger
```

4. تنفيذ الاختبار:

```http
POST /benchmark/reset
POST /demo/benchmark/full
GET  /benchmark/metrics
GET  /benchmark/chart/svg
GET  /benchmark/report
```

## الصور المطلوبة للتقرير

- `Req10_01_Reset.png`
- `Req10_02_Full_Benchmark_Result.png`
- `Req10_03_Final_Metrics.png`
- `Req10_04_Benchmark_Chart.png`
- `Req10_05_Markdown_Report.png`

## النتيجة المتوقعة

يجب أن يظهر:

```json
"benchmarkPassed": true
```

ويجب أن تظهر مقارنة رقمية مثل:

```json
"beforeAverageLatencyMs": 140,
"afterAverageLatencyMs": 1,
"latencyReductionPercent": 99,
"speedupFactor": 100
```

الأرقام الدقيقة تختلف حسب الجهاز، لكن المهم أن يكون هناك تحسن واضح وأن يتم تحديد الاختناق وسبب الاختناق والتحسين المستخدم.

## الخلاصة
تم تحقيق الطلب العاشر عبر Benchmark واضح قبل/بعد، وتحديد Bottleneck رقمي، ثم إثبات أن Redis Cache و Parallel Chunk Processing حسّنا زمن الاستجابة بشكل واضح.
