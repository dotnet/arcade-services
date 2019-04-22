import { TestBed } from '@angular/core/testing';

import { RouterEventHandlerService } from './router-event-handler.service';

describe('RouterEventHandlerService', () => {
  beforeEach(() => TestBed.configureTestingModule({}));

  it('should be created', () => {
    const service: RouterEventHandlerService = TestBed.get(RouterEventHandlerService);
    expect(service).toBeTruthy();
  });
});
