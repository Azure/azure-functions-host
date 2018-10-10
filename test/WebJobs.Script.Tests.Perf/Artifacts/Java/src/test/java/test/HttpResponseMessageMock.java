package test;

import com.microsoft.azure.functions.*;

import java.util.Map;

/**
 * The mock for HttpResponseMessage, can be used in unit tests to verify if the returned response by HTTP trigger function is correct or not.
 */
public class HttpResponseMessageMock implements HttpResponseMessage {
    private HttpStatus status;
    private Map<String, String> headers;
    private Object body;

    public HttpResponseMessageMock(HttpStatus status, Map<String, String> headers, Object body) {
        this.status = status;
        this.headers = headers;
        this.body = body;
    }
    
    @Override
    public HttpStatus getStatus() {
        return this.status;
    }
    @Override
    public String getHeader(String key) {
        return this.headers.get(key);
    }
    @Override
    public Object getBody() {
        return this.body;
    }

    public static class HttpResponseMessageBuilderMock implements HttpResponseMessage.Builder {
        private HttpStatus status;
        private Map<String, String> headers;
        private Object body;

        @Override
        public HttpResponseMessage.Builder status(HttpStatus status) {
            this.status = status;
            return this;
        }
        @Override
        public HttpResponseMessage.Builder header(String key, String value) {
            this.headers.put(key, value);
            return this;
        }
        @Override
        public HttpResponseMessage.Builder body(Object body) {
            this.body = body;
            return this;
        }
        @Override
        public HttpResponseMessage build() {
            return new HttpResponseMessageMock(this.status, this.headers, this.body);
        }
    }
}
