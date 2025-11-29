# AdminApi

All URIs are relative to *https://localhost:7103*

| Method | HTTP request | Description |
|------------- | ------------- | -------------|
| [**adminGetSessionsGet**](AdminApi.md#admingetsessionsget) | **GET** /Admin/GetSessions |  |



## adminGetSessionsGet

> SessionListApiResponse adminGetSessionsGet()



### Example

```ts
import {
  Configuration,
  AdminApi,
} from '';
import type { AdminGetSessionsGetRequest } from '';

async function example() {
  console.log("🚀 Testing  SDK...");
  const api = new AdminApi();

  try {
    const data = await api.adminGetSessionsGet();
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

[**SessionListApiResponse**](SessionListApiResponse.md)

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

