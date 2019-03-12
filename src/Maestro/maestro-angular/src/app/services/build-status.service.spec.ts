import { TestBed, getTestBed } from "@angular/core/testing";
import { HttpClientTestingModule, HttpTestingController } from "@angular/common/http/testing";

import { BuildStatusService } from "./build-status.service";

describe("BuildStatusService", () => {
  let httpMock: HttpTestingController;
  beforeEach(() => TestBed.configureTestingModule({
    imports: [
      HttpClientTestingModule,
    ]
  }));
  beforeEach(() => {
    httpMock = TestBed.get(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  })

  it("should be created", () => {
    const service: BuildStatusService = TestBed.get(BuildStatusService);
    expect(service).toBeTruthy();
  });
});
