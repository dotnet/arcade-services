import { TestBed } from '@angular/core/testing';

import { RouterEventHandlerService } from './router-event-handler.service';
import { RouterTestingModule } from '@angular/router/testing';

describe('RouterEventHandlerService', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [
        RouterTestingModule,
      ],
    });
  });

  it('should be created', () => {
    const service: RouterEventHandlerService = TestBed.get(RouterEventHandlerService);
    expect(service).toBeTruthy();
  });
});
