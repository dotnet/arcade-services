import { async, ComponentFixture, TestBed } from '@angular/core/testing';

import { SubscriptionsTableComponent } from './subscriptions-table.component';

describe('SubscriptionsTableComponent', () => {
  let component: SubscriptionsTableComponent;
  let fixture: ComponentFixture<SubscriptionsTableComponent>;

  beforeEach(async(() => {
    TestBed.configureTestingModule({
      declarations: [ SubscriptionsTableComponent ]
    })
    .compileComponents();
  }));

  beforeEach(() => {
    fixture = TestBed.createComponent(SubscriptionsTableComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
