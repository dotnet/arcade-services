import { TestBed } from "@angular/core/testing";

import { BuildStatusService } from "./build-status.service";

describe("BuildStatusService", () => {
  beforeEach(() => TestBed.configureTestingModule({}));

  it("should be created", () => {
    const service: BuildStatusService = TestBed.get(BuildStatusService);
    expect(service).toBeTruthy();
  });
});
