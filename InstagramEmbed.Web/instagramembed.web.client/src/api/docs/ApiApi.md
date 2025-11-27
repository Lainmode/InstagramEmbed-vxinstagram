# ApiApi

All URIs are relative to *https://localhost:7103*

| Method | HTTP request | Description |
|------------- | ------------- | -------------|
| [**apiGetSessionsGet**](ApiApi.md#apigetsessionsget) | **GET** /Api/GetSessions |  |



## apiGetSessionsGet

> Array&lt;Session&gt; apiGetSessionsGet()



### Example

```ts
import {
  Configuration,
  ApiApi,
} from '';
import type { ApiGetSessionsGetRequest } from '';

async function example() {
  console.log("🚀 Testing  SDK...");
  const api = new ApiApi();

  try {
    const data = await api.apiGetSessionsGet();
    console.log(data);
  } catch (error) {
    console.error(error);
  }
}

// Run the test
example().catch(console.error);
```

### Parameters

This endpoint does not need any parameter.

### Return type

[**Array&lt;Session&gt;**](Session.md)

### Authorization

No authorization required

### HTTP request headers

- **Content-Type**: Not defined
- **Accept**: `text/plain`, `application/json`, `text/json`


### HTTP response details
| Status code | Description | Response headers |
|-------------|-------------|------------------|
| **200** | Success |  -  |

[[Back to top]](#) [[Back to API list]](../README.md#api-endpoints) [[Back to Model list]](../README.md#models) [[Back to README]](../README.md)

